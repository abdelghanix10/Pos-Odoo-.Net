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

public record PosCategoryDto(
    int OdooId,
    string Name,
    int? ParentCategoryId,
    string? ParentCategoryName,
    string? LinkedPosConfigs,
    int ColorIndex,
    string ColorHex,
    int Sequence,
    string? ImageBase64
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

            CREATE TABLE IF NOT EXISTS PosCategories (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                ParentCategoryId INTEGER,
                ParentCategoryName TEXT,
                LinkedPosConfigs TEXT,
                ColorIndex INTEGER DEFAULT 0,
                ColorHex TEXT DEFAULT '#9CA3AF',
                Sequence INTEGER DEFAULT 10,
                ImageBase64 TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );
            """;
        command.ExecuteNonQuery();

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastCategoriesSyncedAt TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { /* Column already exists */ }

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

        // Seed initial categories if table is empty
        using var checkCats = connection.CreateCommand();
        checkCats.CommandText = "SELECT COUNT(*) FROM PosCategories;";
        long catCount = (long)(checkCats.ExecuteScalar() ?? 0L);
        if (catCount == 0)
        {
            SeedDefaultCategories(connection);
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

    private static void SeedDefaultCategories(SqliteConnection connection)
    {
        var defaultCategories = new (int odooId, string name, string? parentName, string linkedShops, int colorIndex, int sequence)[]
        {
            (1, "Misc", null, "Furniture Shop", 9, 1),
            (2, "Desks", null, "Furniture Shop", 3, 2),
            (3, "Chairs", null, "Furniture Shop", 1, 3),
            (4, "Upper body", null, "Clothes Shop", 5, 4),
            (5, "Lower body", null, "Clothes Shop", 10, 5),
            (6, "Others", null, "Clothes Shop", 3, 6),
            (7, "Breads", null, "Bakery Shop", 7, 7),
            (8, "Pastries", null, "Bakery Shop", 8, 8)
        };

        foreach (var c in defaultCategories)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO PosCategories (OdooId, Name, ParentCategoryId, ParentCategoryName, LinkedPosConfigs, ColorIndex, ColorHex, Sequence)
                VALUES (@odooId, @name, NULL, @parentName, @shops, @colorIndex, @colorHex, @sequence);
                """;
            insert.Parameters.AddWithValue("@odooId", c.odooId);
            insert.Parameters.AddWithValue("@name", c.name);
            insert.Parameters.AddWithValue("@parentName", (object?)c.parentName ?? DBNull.Value);
            insert.Parameters.AddWithValue("@shops", c.linkedShops);
            insert.Parameters.AddWithValue("@colorIndex", c.colorIndex);
            insert.Parameters.AddWithValue("@colorHex", PosCategoryRecord.GetOdooColorHex(c.colorIndex));
            insert.Parameters.AddWithValue("@sequence", c.sequence);
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
    /// <summary>
    /// Synchronizes users from Odoo into the local SQLite database.
    /// Upserts users present in Odoo, and DELETES any local user not present in Odoo Online.
    /// Preserves existing local PIN and Password values.
    /// Returns (importedCount, updatedCount, deletedCount).
    /// </summary>
    public async Task<(int imported, int updated, int deleted)> UpsertOdooUsersAsync(List<OdooUserDto> odooUsers)
    {
        int imported = 0;
        int updated = 0;
        int deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = odooUsers.Select(u => u.OdooId).ToHashSet();
        var odooLogins = odooUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.Login))
            .Select(u => u.Login.Trim().ToLowerInvariant())
            .ToHashSet();

        // 1. Upsert users from Odoo
        foreach (var odooUser in odooUsers)
        {
            // Check if user already exists by OdooId or Login
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = """
                SELECT Id, Pin, Password, AvatarBase64
                FROM Users
                WHERE (OdooId IS NOT NULL AND OdooId = @odooId) OR LOWER(Login) = @login
                LIMIT 1;
                """;
            checkCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
            checkCmd.Parameters.AddWithValue("@login", odooUser.Login.Trim().ToLowerInvariant());

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

        // 2. DELETE all users NOT in Odoo Online
        // Only run reconciliation deletion if we have received users from Odoo (protects against empty results)
        if (odooUsers.Count > 0)
        {
            var usersToDelete = new List<long>();
            await using (var fetchAllCmd = connection.CreateCommand())
            {
                fetchAllCmd.Transaction = transaction;
                fetchAllCmd.CommandText = "SELECT Id, OdooId, Login FROM Users;";
                await using var reader = await fetchAllCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    long id = reader.GetInt64(0);
                    int? odooId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                    string login = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim().ToLowerInvariant();

                    bool existsInOdoo = (odooId.HasValue && odooIds.Contains(odooId.Value)) ||
                                        (!string.IsNullOrEmpty(login) && odooLogins.Contains(login));

                    if (!existsInOdoo)
                    {
                        usersToDelete.Add(id);
                    }
                }
            }

            foreach (long deleteId in usersToDelete)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM Users WHERE Id = @id;";
                delCmd.Parameters.AddWithValue("@id", deleteId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    // =========================================================================
    // POS CATEGORIES REPOSITORY
    // =========================================================================
    public async Task<List<PosCategoryRecord>> LoadAllCategoriesAsync()
    {
        var list = new List<PosCategoryRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, Name, ParentCategoryId, ParentCategoryName, LinkedPosConfigs, ColorIndex, ColorHex, Sequence, ImageBase64
            FROM PosCategories
            ORDER BY Sequence ASC, Name ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            long id = reader.GetInt64(0);
            int? odooId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            string name = reader.GetString(2);
            int? parentId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            string? parentName = reader.IsDBNull(4) ? null : reader.GetString(4);
            string? linkedShops = reader.IsDBNull(5) ? null : reader.GetString(5);
            int colorIndex = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
            string colorHex = reader.IsDBNull(7) ? PosCategoryRecord.GetOdooColorHex(colorIndex) : reader.GetString(7);
            int seq = reader.IsDBNull(8) ? 10 : reader.GetInt32(8);
            string? imgBase64 = reader.IsDBNull(9) ? null : reader.GetString(9);

            var cat = new PosCategoryRecord
            {
                Id = id,
                OdooId = odooId,
                Name = name,
                ParentCategoryId = parentId,
                ParentCategoryName = parentName,
                LinkedPosConfigs = linkedShops,
                ColorIndex = colorIndex,
                ColorHex = colorHex,
                Sequence = seq,
                ImageBase64 = imgBase64,
                ImageBitmap = PosImageUtils.Base64ToBitmapImage(imgBase64)
            };
            list.Add(cat);
        }

        return list;
    }

    public async Task SaveCategoryAsync(PosCategoryRecord category)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        if (category.Id > 0)
        {
            cmd.CommandText = """
                UPDATE PosCategories
                SET Name = @name,
                    ParentCategoryId = @parentId,
                    ParentCategoryName = @parentName,
                    LinkedPosConfigs = @shops,
                    ColorIndex = @colorIndex,
                    ColorHex = @colorHex,
                    Sequence = @sequence,
                    ImageBase64 = @image,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", category.Id);
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO PosCategories (OdooId, Name, ParentCategoryId, ParentCategoryName, LinkedPosConfigs, ColorIndex, ColorHex, Sequence, ImageBase64)
                VALUES (@odooId, @name, @parentId, @parentName, @shops, @colorIndex, @colorHex, @sequence, @image);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("@odooId", (object?)category.OdooId ?? DBNull.Value);
        }

        cmd.Parameters.AddWithValue("@name", category.Name);
        cmd.Parameters.AddWithValue("@parentId", (object?)category.ParentCategoryId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@parentName", (object?)category.ParentCategoryName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@shops", (object?)category.LinkedPosConfigs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@colorIndex", category.ColorIndex);
        cmd.Parameters.AddWithValue("@colorHex", category.ColorHex);
        cmd.Parameters.AddWithValue("@sequence", category.Sequence);
        cmd.Parameters.AddWithValue("@image", (object?)category.ImageBase64 ?? DBNull.Value);

        if (category.Id > 0)
        {
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            var newId = await cmd.ExecuteScalarAsync();
            if (newId != null && long.TryParse(newId.ToString(), out long generatedId))
            {
                category.Id = generatedId;
            }
        }
    }

    public async Task DeleteCategoryAsync(long id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PosCategories WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Synchronizes categories from Odoo Online into local SQLite.
    /// Upserts categories present in Odoo, and deletes local categories not in Odoo.
    /// Returns (imported, updated, deleted).
    /// </summary>
    public async Task<(int imported, int updated, int deleted)> UpsertOdooCategoriesAsync(List<PosCategoryDto> categories)
    {
        int imported = 0;
        int updated = 0;
        int deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = categories.Select(c => c.OdooId).ToHashSet();
        var categoryNames = categories
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => c.Name.Trim().ToLowerInvariant())
            .ToHashSet();

        foreach (var cat in categories)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = """
                SELECT Id, ImageBase64
                FROM PosCategories
                WHERE (OdooId IS NOT NULL AND OdooId = @odooId) OR LOWER(Name) = @name
                LIMIT 1;
                """;
            checkCmd.Parameters.AddWithValue("@odooId", cat.OdooId);
            checkCmd.Parameters.AddWithValue("@name", cat.Name.Trim().ToLowerInvariant());

            long? existingId = null;

            await using (var reader = await checkCmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    existingId = reader.GetInt64(0);
                }
            }

            if (existingId.HasValue)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE PosCategories
                    SET OdooId = @odooId,
                        Name = @name,
                        ParentCategoryId = @parentId,
                        ParentCategoryName = @parentName,
                        LinkedPosConfigs = @shops,
                        ColorIndex = @colorIndex,
                        ColorHex = @colorHex,
                        Sequence = @sequence,
                        ImageBase64 = COALESCE(@image, ImageBase64),
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = @id;
                    """;
                updateCmd.Parameters.AddWithValue("@odooId", cat.OdooId);
                updateCmd.Parameters.AddWithValue("@name", cat.Name);
                updateCmd.Parameters.AddWithValue("@parentId", (object?)cat.ParentCategoryId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@parentName", (object?)cat.ParentCategoryName ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@shops", (object?)cat.LinkedPosConfigs ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@colorIndex", cat.ColorIndex);
                updateCmd.Parameters.AddWithValue("@colorHex", cat.ColorHex);
                updateCmd.Parameters.AddWithValue("@sequence", cat.Sequence);
                updateCmd.Parameters.AddWithValue("@image", (object?)cat.ImageBase64 ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@id", existingId.Value);

                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO PosCategories (OdooId, Name, ParentCategoryId, ParentCategoryName, LinkedPosConfigs, ColorIndex, ColorHex, Sequence, ImageBase64)
                    VALUES (@odooId, @name, @parentId, @parentName, @shops, @colorIndex, @colorHex, @sequence, @image);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", cat.OdooId);
                insertCmd.Parameters.AddWithValue("@name", cat.Name);
                insertCmd.Parameters.AddWithValue("@parentId", (object?)cat.ParentCategoryId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@parentName", (object?)cat.ParentCategoryName ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@shops", (object?)cat.LinkedPosConfigs ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@colorIndex", cat.ColorIndex);
                insertCmd.Parameters.AddWithValue("@colorHex", cat.ColorHex);
                insertCmd.Parameters.AddWithValue("@sequence", cat.Sequence);
                insertCmd.Parameters.AddWithValue("@image", (object?)cat.ImageBase64 ?? DBNull.Value);

                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        // Delete categories not in Odoo Online
        if (categories.Count > 0)
        {
            var toDelete = new List<long>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT Id, OdooId, Name FROM PosCategories;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    long id = reader.GetInt64(0);
                    int? odooId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                    string name = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim().ToLowerInvariant();

                    bool existsInOdoo = (odooId.HasValue && odooIds.Contains(odooId.Value)) ||
                                        (!string.IsNullOrEmpty(name) && categoryNames.Contains(name));
                    if (!existsInOdoo)
                    {
                        toDelete.Add(id);
                    }
                }
            }

            foreach (var delId in toDelete)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM PosCategories WHERE Id = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateCategorySyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE OdooConfig
            SET LastCategoriesSyncedAt = @syncedAt
            WHERE Id = 1;
            """;
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastCategorySyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastCategoriesSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }
}

