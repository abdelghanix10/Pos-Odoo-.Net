using System.IO;
using Microsoft.Data.Sqlite;

namespace OdooPosLogin;

public record OdooConfigRecord(
    string ServerUrl,
    string DatabaseName,
    string Username,
    string ApiKey,
    string? LastSyncedAt,
    string? LastStatus
);

public record OdooUserDto(
    int OdooId,
    string Name,
    string Login,
    string Email,
    string Phone,
    string? AvatarBase64
);

public class SqliteDatabaseService
{
    private static readonly Lazy<SqliteDatabaseService> _instance = new(() => new SqliteDatabaseService());
    public static SqliteDatabaseService Instance => _instance.Value;

    private readonly string _connectionString;

    private SqliteDatabaseService()
    {
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pos_data.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS OdooConfig (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                ServerUrl TEXT NOT NULL,
                DatabaseName TEXT NOT NULL,
                Username TEXT NOT NULL,
                ApiKey TEXT,
                LastSyncedAt TEXT,
                LastStatus TEXT
            );

            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER,
                Name TEXT NOT NULL,
                Login TEXT NOT NULL UNIQUE,
                Email TEXT,
                Phone TEXT,
                Password TEXT,
                Pin TEXT,
                Status TEXT DEFAULT 'Confirmed',
                AvatarBase64 TEXT,
                IsVisibleInPos INTEGER DEFAULT 1,
                IsPurpleTheme INTEGER DEFAULT 1
            );
            """;
        command.ExecuteNonQuery();

        // Seed initial Odoo config if missing
        using var checkConfig = connection.CreateCommand();
        checkConfig.CommandText = "SELECT COUNT(*) FROM OdooConfig WHERE Id = 1;";
        long configCount = (long)(checkConfig.ExecuteScalar() ?? 0L);
        if (configCount == 0)
        {
            using var seedConfig = connection.CreateCommand();
            seedConfig.CommandText = """
                INSERT INTO OdooConfig (Id, ServerUrl, DatabaseName, Username, ApiKey, LastSyncedAt, LastStatus)
                VALUES (1, 'https://demo.odoo.com', 'demo', 'admin', '', NULL, 'Not Configured');
                """;
            seedConfig.ExecuteNonQuery();
        }

        // Seed initial demo users if table is empty
        using var checkUsers = connection.CreateCommand();
        checkUsers.CommandText = "SELECT COUNT(*) FROM Users;";
        long userCount = (long)(checkUsers.ExecuteScalar() ?? 0L);
        if (userCount == 0)
        {
            SeedDefaultUsers(connection);
        }
    }

    private static void SeedDefaultUsers(SqliteConnection connection)
    {
        var defaultUsers = new[]
        {
            (1, "Administrator", "admin", "admin@yourcompany.example.com", "+1 555-0199", "1234", "Confirmed", 1, 1),
            (2, "Marc Demo", "demo", "demo@yourcompany.example.com", "+1 555-0144", "1234", "Confirmed", 1, 0),
            (3, "Mitchell Admin", "mitchell", "mitchell@yourcompany.example.com", "+1 555-0182", "1234", "Confirmed", 1, 1),
            (4, "Colleague Cashier", "cashier", "cashier@yourcompany.example.com", "+1 555-0120", "", "Invited", 1, 0)
        };

        foreach (var u in defaultUsers)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO Users (OdooId, Name, Login, Email, Phone, Password, Pin, Status, IsVisibleInPos, IsPurpleTheme)
                VALUES (@odooId, @name, @login, @email, @phone, '••••••••', @pin, @status, @visible, @theme);
                """;
            insert.Parameters.AddWithValue("@odooId", u.Item1);
            insert.Parameters.AddWithValue("@name", u.Item2);
            insert.Parameters.AddWithValue("@login", u.Item3);
            insert.Parameters.AddWithValue("@email", u.Item4);
            insert.Parameters.AddWithValue("@phone", u.Item5);
            insert.Parameters.AddWithValue("@pin", u.Item6);
            insert.Parameters.AddWithValue("@status", u.Item7);
            insert.Parameters.AddWithValue("@visible", u.Item8);
            insert.Parameters.AddWithValue("@theme", u.Item9);
            insert.ExecuteNonQuery();
        }
    }

    // =========================================================================
    // ODOO CONFIG REPOSITORY
    // =========================================================================
    public async Task<OdooConfigRecord> GetOdooConfigAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ServerUrl, DatabaseName, Username, ApiKey, LastSyncedAt, LastStatus
            FROM OdooConfig WHERE Id = 1;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new OdooConfigRecord(
                reader.IsDBNull(0) ? "" : reader.GetString(0),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)
            );
        }

        return new OdooConfigRecord("https://demo.odoo.com", "demo", "admin", "", null, "Not Configured");
    }

    public async Task SaveOdooConfigAsync(OdooConfigRecord config)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OdooConfig (Id, ServerUrl, DatabaseName, Username, ApiKey, LastSyncedAt, LastStatus)
            VALUES (1, @url, @db, @user, @key, @synced, @status)
            ON CONFLICT(Id) DO UPDATE SET
                ServerUrl = excluded.ServerUrl,
                DatabaseName = excluded.DatabaseName,
                Username = excluded.Username,
                ApiKey = excluded.ApiKey;
            """;
        command.Parameters.AddWithValue("@url", config.ServerUrl.Trim());
        command.Parameters.AddWithValue("@db", config.DatabaseName.Trim());
        command.Parameters.AddWithValue("@user", config.Username.Trim());
        command.Parameters.AddWithValue("@key", config.ApiKey ?? "");
        command.Parameters.AddWithValue("@synced", (object?)config.LastSyncedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("@status", (object?)config.LastStatus ?? "Saved");

        await command.ExecuteNonQueryAsync();
    }

    public async Task UpdateSyncStatusAsync(DateTime syncTime, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OdooConfig
            SET LastSyncedAt = @synced, LastStatus = @status
            WHERE Id = 1;
            """;
        command.Parameters.AddWithValue("@synced", syncTime.ToString("yyyy-MM-dd HH:mm:ss"));
        command.Parameters.AddWithValue("@status", status);

        await command.ExecuteNonQueryAsync();
    }

    // =========================================================================
    // USERS REPOSITORY (LOAD & UPSERT)
    // =========================================================================
    public async Task<List<PosUserRecord>> LoadAllUsersAsync()
    {
        var users = new List<PosUserRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OdooId, Name, Login, Email, Phone, Password, Pin, Status, AvatarBase64, IsVisibleInPos, IsPurpleTheme
            FROM Users
            ORDER BY Name ASC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            int? odooId = reader.IsDBNull(0) ? null : reader.GetInt32(0);
            string name = reader.GetString(1);
            string login = reader.GetString(2);
            string email = reader.IsDBNull(3) ? "" : reader.GetString(3);
            string phone = reader.IsDBNull(4) ? "" : reader.GetString(4);
            string password = reader.IsDBNull(5) ? "••••••••" : reader.GetString(5);
            string pin = reader.IsDBNull(6) ? "" : reader.GetString(6);
            string status = reader.IsDBNull(7) ? "Confirmed" : reader.GetString(7);
            string? avatarBase64 = reader.IsDBNull(8) ? null : reader.GetString(8);
            bool isVisible = reader.IsDBNull(9) || reader.GetInt32(9) == 1;
            bool isPurple = reader.IsDBNull(10) || reader.GetInt32(10) == 1;

            var user = new PosUserRecord
            {
                OdooId = odooId,
                Name = name,
                Login = login,
                Email = email,
                Phone = phone,
                Password = password,
                Pin = pin,
                Status = status,
                AvatarBase64 = avatarBase64,
                AvatarBitmap = PosImageUtils.Base64ToBitmapImage(avatarBase64),
                IsVisibleInPos = isVisible,
                IsPurpleTheme = isPurple
            };

            users.Add(user);
        }

        return users;
    }

    public async Task SaveUserRecordAsync(PosUserRecord user)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Users (OdooId, Name, Login, Email, Phone, Password, Pin, Status, AvatarBase64, IsVisibleInPos, IsPurpleTheme)
            VALUES (@odooId, @name, @login, @email, @phone, @pass, @pin, @status, @avatar, @visible, @theme)
            ON CONFLICT(Login) DO UPDATE SET
                Name = excluded.Name,
                Email = excluded.Email,
                Phone = excluded.Phone,
                Password = excluded.Password,
                Pin = excluded.Pin,
                Status = excluded.Status,
                AvatarBase64 = excluded.AvatarBase64,
                IsVisibleInPos = excluded.IsVisibleInPos,
                IsPurpleTheme = excluded.IsPurpleTheme;
            """;

        command.Parameters.AddWithValue("@odooId", (object?)user.OdooId ?? DBNull.Value);
        command.Parameters.AddWithValue("@name", user.Name);
        command.Parameters.AddWithValue("@login", user.Login);
        command.Parameters.AddWithValue("@email", user.Email);
        command.Parameters.AddWithValue("@phone", user.Phone);
        command.Parameters.AddWithValue("@pass", user.Password);
        command.Parameters.AddWithValue("@pin", user.Pin);
        command.Parameters.AddWithValue("@status", user.Status);
        command.Parameters.AddWithValue("@avatar", (object?)user.AvatarBase64 ?? DBNull.Value);
        command.Parameters.AddWithValue("@visible", user.IsVisibleInPos ? 1 : 0);
        command.Parameters.AddWithValue("@theme", user.IsPurpleTheme ? 1 : 0);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteUserRecordAsync(string login)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Users WHERE Login = @login;";
        command.Parameters.AddWithValue("@login", login);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Upserts users synchronized from Odoo into the local SQLite database.
    /// Preserves existing local PIN and Password values.
    /// Returns (importedCount, updatedCount).
    /// </summary>
    public async Task<(int imported, int updated)> UpsertOdooUsersAsync(List<OdooUserDto> odooUsers)
    {
        int imported = 0;
        int updated = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        foreach (var odooUser in odooUsers)
        {
            // Check if user already exists by OdooId or Login
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = """
                SELECT Id, Pin, Password, AvatarBase64
                FROM Users
                WHERE (OdooId IS NOT NULL AND OdooId = @odooId) OR Login = @login
                LIMIT 1;
                """;
            checkCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
            checkCmd.Parameters.AddWithValue("@login", odooUser.Login);

            long? existingId = null;
            string existingPin = "";
            string existingPassword = "••••••••";
            string? existingAvatar = null;

            await using (var reader = await checkCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    existingId = reader.GetInt64(0);
                    existingPin = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    existingPassword = reader.IsDBNull(2) ? "••••••••" : reader.GetString(2);
                    existingAvatar = reader.IsDBNull(3) ? null : reader.GetString(3);
                }
            }

            if (existingId.HasValue)
            {
                // UPDATE existing record, PRESERVING existing PIN and Password
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE Users
                    SET OdooId = @odooId,
                        Name = @name,
                        Login = @login,
                        Email = @email,
                        Phone = @phone,
                        AvatarBase64 = COALESCE(@avatar, AvatarBase64)
                    WHERE Id = @id;
                    """;
                updateCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
                updateCmd.Parameters.AddWithValue("@name", odooUser.Name);
                updateCmd.Parameters.AddWithValue("@login", odooUser.Login);
                updateCmd.Parameters.AddWithValue("@email", odooUser.Email);
                updateCmd.Parameters.AddWithValue("@phone", odooUser.Phone);
                updateCmd.Parameters.AddWithValue("@avatar", (object?)odooUser.AvatarBase64 ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@id", existingId.Value);

                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                // INSERT new user
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO Users (OdooId, Name, Login, Email, Phone, Password, Pin, Status, AvatarBase64, IsVisibleInPos, IsPurpleTheme)
                    VALUES (@odooId, @name, @login, @email, @phone, @pass, @pin, 'Confirmed', @avatar, 1, 1);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
                insertCmd.Parameters.AddWithValue("@name", odooUser.Name);
                insertCmd.Parameters.AddWithValue("@login", odooUser.Login);
                insertCmd.Parameters.AddWithValue("@email", odooUser.Email);
                insertCmd.Parameters.AddWithValue("@phone", odooUser.Phone);
                insertCmd.Parameters.AddWithValue("@pass", "••••••••");
                insertCmd.Parameters.AddWithValue("@pin", "");
                insertCmd.Parameters.AddWithValue("@avatar", (object?)odooUser.AvatarBase64 ?? DBNull.Value);

                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        await transaction.CommitAsync();
        return (imported, updated);
    }
}

