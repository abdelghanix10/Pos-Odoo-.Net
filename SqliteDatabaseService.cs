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

public record ProductAttributeDto(
    int OdooId,
    string Name,
    string DisplayType,
    string CreateVariant,
    int NumberRelatedProducts,
    List<int> ValueIds
);

public record ProductAttributeValueDto(
    int OdooId,
    int AttributeOdooId,
    string Name,
    bool IsCustom,
    string? HtmlColor,
    string? ImageBase64,
    double DefaultExtraPrice
);

public record ProductTagDto(
    int OdooId,
    string Name,
    int ColorIndex,
    int ProductsCount
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

            CREATE TABLE IF NOT EXISTS ProductAttributes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                DisplayType TEXT DEFAULT 'radio',
                CreateVariant TEXT DEFAULT 'always',
                NumberRelatedProducts INTEGER DEFAULT 0,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS ProductAttributeValues (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                AttributeOdooId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                IsCustom INTEGER DEFAULT 0,
                HtmlColor TEXT,
                ImageBase64 TEXT,
                DefaultExtraPrice REAL DEFAULT 0.0,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS ProductTags (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                ColorIndex INTEGER DEFAULT 0,
                ProductsCount INTEGER DEFAULT 0,
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

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastAttributesSyncedAt TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { /* Column already exists */ }

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastTagsSyncedAt TEXT;";
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

        // Seed initial attributes if table is empty
        using var checkAttrs = connection.CreateCommand();
        checkAttrs.CommandText = "SELECT COUNT(*) FROM ProductAttributes;";
        long attrCount = (long)(checkAttrs.ExecuteScalar() ?? 0L);
        if (attrCount == 0)
        {
            SeedDefaultAttributes(connection);
        }

        // Seed initial tags if table is empty
        using var checkTags = connection.CreateCommand();
        checkTags.CommandText = "SELECT COUNT(*) FROM ProductTags;";
        long tagCount = (long)(checkTags.ExecuteScalar() ?? 0L);
        if (tagCount == 0)
        {
            SeedDefaultTags(connection);
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

    private static void SeedDefaultAttributes(SqliteConnection connection)
    {
        var attributes = new (int odooId, string name, string displayType, string createVariant, int relatedProducts)[]
        {
            (1, "Brand", "radio", "always", 0),
            (2, "Size", "pills", "always", 5),
            (3, "Color", "color", "always", 3),
            (4, "Material", "radio", "always", 0),
            (13, "Shoes size", "pills", "no_variant", 2)
        };

        foreach (var a in attributes)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ProductAttributes (OdooId, Name, DisplayType, CreateVariant, NumberRelatedProducts)
                VALUES (@odooId, @name, @displayType, @createVariant, @relatedProducts);
                """;
            cmd.Parameters.AddWithValue("@odooId", a.odooId);
            cmd.Parameters.AddWithValue("@name", a.name);
            cmd.Parameters.AddWithValue("@displayType", a.displayType);
            cmd.Parameters.AddWithValue("@createVariant", a.createVariant);
            cmd.Parameters.AddWithValue("@relatedProducts", a.relatedProducts);
            cmd.ExecuteNonQuery();
        }

        var values = new (int odooId, int attrOdooId, string name, bool isCustom, string? htmlColor, double extraPrice)[]
        {
            // Brand (1)
            (1, 1, "Adidas", false, null, 0.0),
            (2, 1, "Apple", false, null, 0.0),
            (3, 1, "Ariel", false, null, 0.0),
            (4, 1, "Canon", false, null, 0.0),
            (5, 1, "Coca-Cola", false, null, 0.0),
            (6, 1, "Nike", false, null, 0.0),
            // Size (2)
            (41, 2, "XS", false, null, 0.0),
            (42, 2, "S", false, null, 0.0),
            (43, 2, "M", false, null, 0.0),
            (44, 2, "L", false, null, 0.0),
            (45, 2, "XL", false, null, 0.0),
            // Color (3)
            (50, 3, "White", false, "#FFFFFF", 0.0),
            (51, 3, "Black", false, "#000000", 0.0),
            (52, 3, "Red", false, "#EF4444", 2.50),
            (53, 3, "Blue", false, "#3B82F6", 2.50),
            (54, 3, "Green", false, "#10B981", 0.0),
            // Material (4)
            (70, 4, "Cotton", false, null, 0.0),
            (71, 4, "Leather", false, null, 15.0),
            (72, 4, "Wood", false, null, 0.0),
            // Shoes size (13)
            (97, 13, "38", false, null, 0.0),
            (98, 13, "39", false, null, 0.0),
            (99, 13, "40", false, null, 0.0),
            (100, 13, "41", false, null, 0.0),
            (101, 13, "42", false, null, 0.0)
        };

        foreach (var v in values)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ProductAttributeValues (OdooId, AttributeOdooId, Name, IsCustom, HtmlColor, DefaultExtraPrice)
                VALUES (@odooId, @attrId, @name, @custom, @color, @extra);
                """;
            cmd.Parameters.AddWithValue("@odooId", v.odooId);
            cmd.Parameters.AddWithValue("@attrId", v.attrOdooId);
            cmd.Parameters.AddWithValue("@name", v.name);
            cmd.Parameters.AddWithValue("@custom", v.isCustom ? 1 : 0);
            cmd.Parameters.AddWithValue("@color", (object?)v.htmlColor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@extra", v.extraPrice);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultTags(SqliteConnection connection)
    {
        var tags = new (int odooId, string name, int colorIndex, int productsCount)[]
        {
            (1, "Bestseller", 1, 12),
            (2, "New Arrival", 10, 8),
            (3, "Organic", 7, 5),
            (4, "Featured", 5, 4),
            (5, "Special Offer", 2, 9)
        };

        foreach (var t in tags)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ProductTags (OdooId, Name, ColorIndex, ProductsCount)
                VALUES (@odooId, @name, @colorIndex, @productsCount);
                """;
            cmd.Parameters.AddWithValue("@odooId", t.odooId);
            cmd.Parameters.AddWithValue("@name", t.name);
            cmd.Parameters.AddWithValue("@colorIndex", t.colorIndex);
            cmd.Parameters.AddWithValue("@productsCount", t.productsCount);
            cmd.ExecuteNonQuery();
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

    // =========================================================================
    // PRODUCT ATTRIBUTES & VALUES REPOSITORY
    // =========================================================================
    public async Task<List<ProductAttributeRecord>> LoadAllAttributesAsync()
    {
        var result = new List<ProductAttributeRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, Name, DisplayType, CreateVariant, NumberRelatedProducts
            FROM ProductAttributes
            ORDER BY Id ASC;
            """;

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var attr = new ProductAttributeRecord
                {
                    Id = reader.GetInt64(0),
                    OdooId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    DisplayType = reader.IsDBNull(3) ? "radio" : reader.GetString(3),
                    CreateVariant = reader.IsDBNull(4) ? "always" : reader.GetString(4),
                    NumberRelatedProducts = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                };
                result.Add(attr);
            }
        }

        // Load values for each attribute
        foreach (var attr in result)
        {
            attr.Values = await LoadAttributeValuesInternalAsync(connection, attr.OdooId);
        }

        return result;
    }

    private static async Task<List<ProductAttributeValueRecord>> LoadAttributeValuesInternalAsync(SqliteConnection connection, int attributeOdooId)
    {
        var values = new List<ProductAttributeValueRecord>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, AttributeOdooId, Name, IsCustom, HtmlColor, ImageBase64, DefaultExtraPrice
            FROM ProductAttributeValues
            WHERE AttributeOdooId = @attrId
            ORDER BY Id ASC;
            """;
        cmd.Parameters.AddWithValue("@attrId", attributeOdooId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var val = new ProductAttributeValueRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.GetInt32(1),
                AttributeOdooId = reader.GetInt32(2),
                Name = reader.GetString(3),
                IsCustom = !reader.IsDBNull(4) && reader.GetInt32(4) == 1,
                HtmlColor = reader.IsDBNull(5) ? null : reader.GetString(5),
                ImageBase64 = reader.IsDBNull(6) ? null : reader.GetString(6),
                DefaultExtraPrice = reader.IsDBNull(7) ? 0.0 : reader.GetDouble(7)
            };
            values.Add(val);
        }

        return values;
    }

    public async Task<List<ProductAttributeValueRecord>> LoadAttributeValuesAsync(int attributeOdooId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return await LoadAttributeValuesInternalAsync(connection, attributeOdooId);
    }

    public async Task<(int attrsImported, int attrsUpdated, int attrsDeleted, int valsImported, int valsUpdated, int valsDeleted)>
        UpsertOdooAttributesAsync(List<ProductAttributeDto> attributes, List<ProductAttributeValueDto> values)
    {
        int attrsImported = 0, attrsUpdated = 0, attrsDeleted = 0;
        int valsImported = 0, valsUpdated = 0, valsDeleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooAttrIds = attributes.Select(a => a.OdooId).ToHashSet();
        var odooValIds = values.Select(v => v.OdooId).ToHashSet();

        // 1. Upsert Attributes
        foreach (var attr in attributes)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ProductAttributes WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", attr.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE ProductAttributes
                    SET Name = @name,
                        DisplayType = @displayType,
                        CreateVariant = @createVariant,
                        NumberRelatedProducts = @relatedProducts,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@name", attr.Name);
                updateCmd.Parameters.AddWithValue("@displayType", attr.DisplayType);
                updateCmd.Parameters.AddWithValue("@createVariant", attr.CreateVariant);
                updateCmd.Parameters.AddWithValue("@relatedProducts", attr.NumberRelatedProducts);
                updateCmd.Parameters.AddWithValue("@odooId", attr.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                attrsUpdated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO ProductAttributes (OdooId, Name, DisplayType, CreateVariant, NumberRelatedProducts)
                    VALUES (@odooId, @name, @displayType, @createVariant, @relatedProducts);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", attr.OdooId);
                insertCmd.Parameters.AddWithValue("@name", attr.Name);
                insertCmd.Parameters.AddWithValue("@displayType", attr.DisplayType);
                insertCmd.Parameters.AddWithValue("@createVariant", attr.CreateVariant);
                insertCmd.Parameters.AddWithValue("@relatedProducts", attr.NumberRelatedProducts);
                await insertCmd.ExecuteNonQueryAsync();
                attrsImported++;
            }
        }

        // 2. Upsert Attribute Values
        foreach (var val in values)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ProductAttributeValues WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", val.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE ProductAttributeValues
                    SET AttributeOdooId = @attrId,
                        Name = @name,
                        IsCustom = @custom,
                        HtmlColor = @color,
                        ImageBase64 = COALESCE(@image, ImageBase64),
                        DefaultExtraPrice = @extra,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@attrId", val.AttributeOdooId);
                updateCmd.Parameters.AddWithValue("@name", val.Name);
                updateCmd.Parameters.AddWithValue("@custom", val.IsCustom ? 1 : 0);
                updateCmd.Parameters.AddWithValue("@color", (object?)val.HtmlColor ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@image", (object?)val.ImageBase64 ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@extra", val.DefaultExtraPrice);
                updateCmd.Parameters.AddWithValue("@odooId", val.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                valsUpdated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO ProductAttributeValues (OdooId, AttributeOdooId, Name, IsCustom, HtmlColor, ImageBase64, DefaultExtraPrice)
                    VALUES (@odooId, @attrId, @name, @custom, @color, @image, @extra);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", val.OdooId);
                insertCmd.Parameters.AddWithValue("@attrId", val.AttributeOdooId);
                insertCmd.Parameters.AddWithValue("@name", val.Name);
                insertCmd.Parameters.AddWithValue("@custom", val.IsCustom ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@color", (object?)val.HtmlColor ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@image", (object?)val.ImageBase64 ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@extra", val.DefaultExtraPrice);
                await insertCmd.ExecuteNonQueryAsync();
                valsImported++;
            }
        }

        // 3. Reconcile / Delete attributes not in Odoo
        if (attributes.Count > 0)
        {
            var delAttrs = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM ProductAttributes;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooAttrIds.Contains(oId)) delAttrs.Add(oId);
                }
            }

            foreach (var delId in delAttrs)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM ProductAttributes WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                attrsDeleted++;
            }
        }

        // 4. Reconcile / Delete values not in Odoo
        if (values.Count > 0)
        {
            var delVals = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM ProductAttributeValues;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooValIds.Contains(oId)) delVals.Add(oId);
                }
            }

            foreach (var delId in delVals)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM ProductAttributeValues WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                valsDeleted++;
            }
        }

        await transaction.CommitAsync();
        return (attrsImported, attrsUpdated, attrsDeleted, valsImported, valsUpdated, valsDeleted);
    }

    public async Task UpdateAttributeSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE OdooConfig
            SET LastAttributesSyncedAt = @syncedAt
            WHERE Id = 1;
            """;
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastAttributeSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastAttributesSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // PRODUCT TAGS REPOSITORY
    // =========================================================================
    public async Task<List<ProductTagRecord>> LoadAllTagsAsync()
    {
        var result = new List<ProductTagRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, Name, ColorIndex, ProductsCount
            FROM ProductTags
            ORDER BY Id ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tag = new ProductTagRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.GetInt32(1),
                Name = reader.GetString(2),
                ColorIndex = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                ProductsCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            };
            result.Add(tag);
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooTagsAsync(List<ProductTagDto> tags)
    {
        int imported = 0, updated = 0, deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooTagIds = tags.Select(t => t.OdooId).ToHashSet();

        foreach (var tag in tags)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ProductTags WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", tag.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE ProductTags
                    SET Name = @name,
                        ColorIndex = @color,
                        ProductsCount = @count,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@name", tag.Name);
                updateCmd.Parameters.AddWithValue("@color", tag.ColorIndex);
                updateCmd.Parameters.AddWithValue("@count", tag.ProductsCount);
                updateCmd.Parameters.AddWithValue("@odooId", tag.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO ProductTags (OdooId, Name, ColorIndex, ProductsCount)
                    VALUES (@odooId, @name, @color, @count);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", tag.OdooId);
                insertCmd.Parameters.AddWithValue("@name", tag.Name);
                insertCmd.Parameters.AddWithValue("@color", tag.ColorIndex);
                insertCmd.Parameters.AddWithValue("@count", tag.ProductsCount);
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        // Reconcile / Delete tags not in Odoo Online
        if (tags.Count > 0)
        {
            var delIds = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM ProductTags;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooTagIds.Contains(oId)) delIds.Add(oId);
                }
            }

            foreach (var delId in delIds)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM ProductTags WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateTagSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE OdooConfig
            SET LastTagsSyncedAt = @syncedAt
            WHERE Id = 1;
            """;
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastTagSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastTagsSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }
}

