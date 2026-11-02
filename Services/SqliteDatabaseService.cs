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

public record ProductDto(
    int OdooId,
    string Name,
    string ProductType,
    decimal ListPrice,
    decimal StandardPrice,
    string DefaultCode,
    string Barcode,
    int? PosCategoryId,
    string? PosCategoryName,
    int VariantCount,
    string? ImageBase64,
    string? TagsJson,
    string? AttributesJson,
    string? ComboChoicesJson
);

public record ComboChoiceDto(
    int OdooId,
    string Name,
    List<int> LineIds
);

public record ComboChoiceLineDto(
    int OdooId,
    int ComboId,
    int? ProductId,
    string ProductName,
    decimal OriginalPrice,
    decimal ExtraPrice
);

public record PosSessionDto(
    int OdooId,
    string Name,
    int? PosConfigId,
    string? PosConfigName,
    int? UserId,
    string? UserName,
    string? UserAvatarBase64,
    DateTime? StartAt,
    DateTime? StopAt,
    string State,
    decimal OpeningBalance,
    decimal ClosingBalance,
    int OrdersCount,
    decimal TotalPaymentsAmount,
    string Employees
);

public record PosOrderDto(
    int OdooId,
    string Name,
    string PosReference,
    string TrackingNumber,
    int? SessionId,
    int? SessionOdooId,
    string SessionName,
    string PosConfigName,
    DateTime? DateOrder,
    int? CashierId,
    string CashierName,
    string? CashierAvatarBase64,
    string FiscalPosition,
    decimal AmountTotal,
    decimal AmountTax,
    decimal AmountPaid,
    decimal Margin,
    decimal MarginRatio,
    string State,
    bool IsEdited,
    string Company,
    string Note,
    string? LinesJson,
    string? PaymentsJson
);

public record PosOrderLineDto(
    int OdooId,
    int OrderOdooId,
    int? ProductId,
    string ProductName,
    bool IsEdited,
    decimal Qty,
    decimal PriceUnit,
    decimal Discount,
    string Taxes,
    decimal PriceSubtotal,
    decimal PriceSubtotalIncl
);

public record PosPaymentDto(
    int OdooId,
    int? OrderOdooId,
    string OrderName,
    int? SessionOdooId,
    string SessionName,
    string CashierName,
    int? PaymentMethodId,
    string PaymentMethodName,
    decimal Amount,
    string CurrencyName,
    string ForeignCurrency,
    DateTime? PaymentDate
);

public record PosConfigDto(
    int OdooId,
    string Name,
    int? CurrentSessionId,
    string? CurrentSessionName,
    string? CurrentSessionState,
    DateTime? LastClosingDate,
    decimal LastClosingBalance,
    decimal OpeningBalance,
    int? ActiveUserId,
    string? ActiveUserName,
    string? ActiveUserAvatar,
    string? RecentSalesPointsJson
);

public record DatabaseCountsSummary(
    int UsersCount,
    int CategoriesCount,
    int AttributesCount,
    int TagsCount,
    int CombosCount,
    int ProductsCount,
    int PosConfigsCount,
    int SessionsCount,
    int OrdersCount,
    int PaymentsCount
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

            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                ProductType TEXT DEFAULT 'consu',
                ListPrice REAL DEFAULT 0.0,
                StandardPrice REAL DEFAULT 0.0,
                DefaultCode TEXT,
                Barcode TEXT,
                PosCategoryId INTEGER,
                PosCategoryName TEXT,
                VariantCount INTEGER DEFAULT 1,
                ImageBase64 TEXT,
                TagsJson TEXT,
                AttributesJson TEXT,
                ComboChoicesJson TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS ComboChoices (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS ComboChoiceLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                ComboId INTEGER NOT NULL,
                ProductId INTEGER,
                ProductName TEXT NOT NULL,
                OriginalPrice REAL DEFAULT 0.0,
                ExtraPrice REAL DEFAULT 0.0,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PosSessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                PosConfigId INTEGER,
                PosConfigName TEXT,
                UserId INTEGER,
                UserName TEXT,
                UserAvatarBase64 TEXT,
                StartAt TEXT,
                StopAt TEXT,
                State TEXT DEFAULT 'opened',
                OpeningBalance REAL DEFAULT 0.0,
                ClosingBalance REAL DEFAULT 0.0,
                OrdersCount INTEGER DEFAULT 0,
                TotalPaymentsAmount REAL DEFAULT 0.0,
                Employees TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PosOrders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                PosReference TEXT,
                TrackingNumber TEXT,
                SessionId INTEGER,
                SessionOdooId INTEGER,
                SessionName TEXT,
                PosConfigName TEXT,
                DateOrder TEXT,
                CashierId INTEGER,
                CashierName TEXT,
                CashierAvatarBase64 TEXT,
                FiscalPosition TEXT DEFAULT '—',
                AmountTotal REAL DEFAULT 0.0,
                AmountTax REAL DEFAULT 0.0,
                AmountPaid REAL DEFAULT 0.0,
                Margin REAL DEFAULT 0.0,
                MarginRatio REAL DEFAULT 0.0,
                State TEXT DEFAULT 'paid',
                IsEdited INTEGER DEFAULT 0,
                Company TEXT DEFAULT 'My Company',
                Note TEXT,
                LinesJson TEXT,
                PaymentsJson TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PosOrderLines (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                OrderId INTEGER,
                OrderOdooId INTEGER,
                ProductId INTEGER,
                ProductName TEXT NOT NULL,
                IsEdited INTEGER DEFAULT 0,
                Qty REAL DEFAULT 1.0,
                PriceUnit REAL DEFAULT 0.0,
                Discount REAL DEFAULT 0.0,
                Taxes TEXT DEFAULT '15%',
                PriceSubtotal REAL DEFAULT 0.0,
                PriceSubtotalIncl REAL DEFAULT 0.0,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PosPayments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                OrderId INTEGER,
                OrderOdooId INTEGER,
                OrderName TEXT,
                SessionId INTEGER,
                SessionOdooId INTEGER,
                SessionName TEXT,
                CashierName TEXT,
                PaymentMethodId INTEGER,
                PaymentMethodName TEXT DEFAULT 'Cash',
                Amount REAL DEFAULT 0.0,
                CurrencyName TEXT DEFAULT 'MAD',
                ForeignCurrency TEXT DEFAULT '—',
                PaymentDate TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PosConfigs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                CurrentSessionId INTEGER,
                CurrentSessionName TEXT,
                CurrentSessionState TEXT DEFAULT 'closed',
                LastClosingDate TEXT,
                LastClosingBalance REAL DEFAULT 0.0,
                OpeningBalance REAL DEFAULT 0.0,
                ActiveUserId INTEGER,
                ActiveUserName TEXT,
                ActiveUserAvatar TEXT,
                RecentSalesPointsJson TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );
            """;
        command.ExecuteNonQuery();

        string[] extraConfigColumns =
        [
            "LastCategoriesSyncedAt TEXT",
            "LastAttributesSyncedAt TEXT",
            "LastTagsSyncedAt TEXT",
            "LastProductsSyncedAt TEXT",
            "LastCombosSyncedAt TEXT",
            "LastSessionsSyncedAt TEXT",
            "LastOrdersSyncedAt TEXT",
            "LastPaymentsSyncedAt TEXT",
            "LastPosConfigsSyncedAt TEXT",
            "HasPurgedLegacyDemoData INTEGER DEFAULT 0"
        ];

        foreach (string colDef in extraConfigColumns)
        {
            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = $"ALTER TABLE OdooConfig ADD COLUMN {colDef};";
                alterCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists */ }
        }

        // Ensure single row in OdooConfig exists (without fake demo credentials if brand new)
        using var checkConfig = connection.CreateCommand();
        checkConfig.CommandText = "SELECT COUNT(*) FROM OdooConfig WHERE Id = 1;";
        long configCount = (long)(checkConfig.ExecuteScalar() ?? 0L);
        if (configCount == 0)
        {
            using var seedConfig = connection.CreateCommand();
            seedConfig.CommandText = """
                INSERT INTO OdooConfig (Id, ServerUrl, DatabaseName, Username, ApiKey, LastSyncedAt, LastStatus, HasPurgedLegacyDemoData)
                VALUES (1, 'http://localhost:8069', '', '', '', NULL, 'Not Configured', 1);
                """;
            seedConfig.ExecuteNonQuery();
        }
        else
        {
            // One-time purge of any previously seeded mock/demo data while keeping OdooConfig credentials intact
            using var checkPurgedCmd = connection.CreateCommand();
            checkPurgedCmd.CommandText = "SELECT COALESCE(HasPurgedLegacyDemoData, 0) FROM OdooConfig WHERE Id = 1;";
            long hasPurged = Convert.ToInt64(checkPurgedCmd.ExecuteScalar() ?? 0L);
            if (hasPurged == 0)
            {
                PurgeAllEntityTablesInternal(connection);
                using var markPurgedCmd = connection.CreateCommand();
                markPurgedCmd.CommandText = "UPDATE OdooConfig SET HasPurgedLegacyDemoData = 1 WHERE Id = 1;";
                markPurgedCmd.ExecuteNonQuery();
            }
        }
    }

    private static void PurgeAllEntityTablesInternal(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM Users;
            DELETE FROM PosCategories;
            DELETE FROM ProductAttributeValues;
            DELETE FROM ProductAttributes;
            DELETE FROM ProductTags;
            DELETE FROM ComboChoiceLines;
            DELETE FROM ComboChoices;
            DELETE FROM Products;
            DELETE FROM PosOrderLines;
            DELETE FROM PosPayments;
            DELETE FROM PosOrders;
            DELETE FROM PosSessions;
            DELETE FROM PosConfigs;

            UPDATE OdooConfig
            SET LastSyncedAt = NULL,
                LastCategoriesSyncedAt = NULL,
                LastAttributesSyncedAt = NULL,
                LastTagsSyncedAt = NULL,
                LastProductsSyncedAt = NULL,
                LastCombosSyncedAt = NULL,
                LastSessionsSyncedAt = NULL,
                LastOrdersSyncedAt = NULL,
                LastPaymentsSyncedAt = NULL,
                LastPosConfigsSyncedAt = NULL
            WHERE Id = 1;
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Clears all synchronized local entity data while keeping Odoo server connection settings intact.
    /// </summary>
    public async Task ClearAllLocalDataAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        PurgeAllEntityTablesInternal(connection);
    }

    /// <summary>
    /// Returns live record counts across all 10 local SQLite entity tables.
    /// </summary>
    public async Task<DatabaseCountsSummary> GetDatabaseCountsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        async Task<int> CountTableAsync(string table)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
        }

        return new DatabaseCountsSummary(
            await CountTableAsync("Users"),
            await CountTableAsync("PosCategories"),
            await CountTableAsync("ProductAttributes"),
            await CountTableAsync("ProductTags"),
            await CountTableAsync("ComboChoices"),
            await CountTableAsync("Products"),
            await CountTableAsync("PosConfigs"),
            await CountTableAsync("PosSessions"),
            await CountTableAsync("PosOrders"),
            await CountTableAsync("PosPayments")
        );
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

        return new OdooConfigRecord("http://localhost:8069", "", "", "", null, "Not Configured");
    }

    public async Task SaveOdooConfigAsync(OdooConfigRecord config)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO OdooConfig (Id, ServerUrl, DatabaseName, Username, ApiKey, LastSyncedAt, LastStatus, HasPurgedLegacyDemoData)
            VALUES (1, @url, @db, @user, @key, @synced, @status, 1)
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
    // 1. USERS REPOSITORY (LOAD & STRICT 1:1 RECONCILIATION WITH ODOO)
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

            users.Add(new PosUserRecord
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
            });
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
    /// Strictly reconciles local Users with Odoo res.users:
    /// - Updates existing users (preserving local PIN, Password, and POS visibility).
    /// - Inserts new Odoo users.
    /// - Deletes any local user that no longer exists in Odoo.
    /// </summary>
    public async Task<(int imported, int updated, int deleted)> UpsertOdooUsersAsync(List<OdooUserDto> odooUsers)
    {
        int imported = 0, updated = 0, deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = odooUsers.Select(u => u.OdooId).ToHashSet();
        var odooLogins = odooUsers
            .Where(u => !string.IsNullOrWhiteSpace(u.Login))
            .Select(u => u.Login.Trim().ToLowerInvariant())
            .ToHashSet();

        foreach (var odooUser in odooUsers)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = """
                SELECT Id
                FROM Users
                WHERE (OdooId IS NOT NULL AND OdooId = @odooId) OR LOWER(Login) = @login
                LIMIT 1;
                """;
            checkCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
            checkCmd.Parameters.AddWithValue("@login", odooUser.Login.Trim().ToLowerInvariant());

            var existingObj = await checkCmd.ExecuteScalarAsync();
            if (existingObj != null && existingObj != DBNull.Value)
            {
                long existingId = Convert.ToInt64(existingObj);
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE Users
                    SET OdooId = @odooId,
                        Name = @name,
                        Login = @login,
                        Email = @email,
                        Phone = @phone,
                        Status = 'Confirmed',
                        AvatarBase64 = @avatar
                    WHERE Id = @id;
                    """;
                updateCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
                updateCmd.Parameters.AddWithValue("@name", odooUser.Name);
                updateCmd.Parameters.AddWithValue("@login", odooUser.Login);
                updateCmd.Parameters.AddWithValue("@email", odooUser.Email);
                updateCmd.Parameters.AddWithValue("@phone", odooUser.Phone);
                updateCmd.Parameters.AddWithValue("@avatar", (object?)odooUser.AvatarBase64 ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@id", existingId);

                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO Users (OdooId, Name, Login, Email, Phone, Password, Pin, Status, AvatarBase64, IsVisibleInPos, IsPurpleTheme)
                    VALUES (@odooId, @name, @login, @email, @phone, @pass, @pin, 'Confirmed', @avatar, 1, @theme);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
                insertCmd.Parameters.AddWithValue("@name", odooUser.Name);
                insertCmd.Parameters.AddWithValue("@login", odooUser.Login);
                insertCmd.Parameters.AddWithValue("@email", odooUser.Email);
                insertCmd.Parameters.AddWithValue("@phone", odooUser.Phone);
                insertCmd.Parameters.AddWithValue("@pass", "••••••••");
                insertCmd.Parameters.AddWithValue("@pin", "");
                insertCmd.Parameters.AddWithValue("@avatar", (object?)odooUser.AvatarBase64 ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@theme", odooUser.OdooId % 2 == 1 ? 1 : 0);

                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        // Remove any local user not present in Odoo
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

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    // =========================================================================
    // 2. POS CATEGORIES REPOSITORY (STRICT 1:1 RECONCILIATION)
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

            list.Add(new PosCategoryRecord
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
            });
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

    public async Task<(int imported, int updated, int deleted)> UpsertOdooCategoriesAsync(List<PosCategoryDto> categories)
    {
        int imported = 0, updated = 0, deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = categories.Select(c => c.OdooId).ToHashSet();

        foreach (var cat in categories)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosCategories WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", cat.OdooId);

            var existingObj = await checkCmd.ExecuteScalarAsync();
            if (existingObj != null && existingObj != DBNull.Value)
            {
                long existingId = Convert.ToInt64(existingObj);
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
                        ImageBase64 = @image,
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
                updateCmd.Parameters.AddWithValue("@id", existingId);

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

        // Strict delete of any local category not in Odoo
        var toDelete = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM PosCategories;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? odooId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!odooId.HasValue || !odooIds.Contains(odooId.Value))
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

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateCategorySyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastCategoriesSyncedAt = @syncedAt WHERE Id = 1;";
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
    // 3. PRODUCT ATTRIBUTES & VALUES REPOSITORY (STRICT 1:1 RECONCILIATION)
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
                result.Add(new ProductAttributeRecord
                {
                    Id = reader.GetInt64(0),
                    OdooId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    DisplayType = reader.IsDBNull(3) ? "radio" : reader.GetString(3),
                    CreateVariant = reader.IsDBNull(4) ? "always" : reader.GetString(4),
                    NumberRelatedProducts = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                });
            }
        }

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
            values.Add(new ProductAttributeValueRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.GetInt32(1),
                AttributeOdooId = reader.GetInt32(2),
                Name = reader.GetString(3),
                IsCustom = !reader.IsDBNull(4) && reader.GetInt32(4) == 1,
                HtmlColor = reader.IsDBNull(5) ? null : reader.GetString(5),
                ImageBase64 = reader.IsDBNull(6) ? null : reader.GetString(6),
                DefaultExtraPrice = reader.IsDBNull(7) ? 0.0 : reader.GetDouble(7)
            });
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

        foreach (var attr in attributes)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ProductAttributes WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", attr.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
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

        foreach (var val in values)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ProductAttributeValues WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", val.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE ProductAttributeValues
                    SET AttributeOdooId = @attrId,
                        Name = @name,
                        IsCustom = @custom,
                        HtmlColor = @color,
                        ImageBase64 = @image,
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

        // Strict delete of attributes not in Odoo
        var delAttrs = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM ProductAttributes;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooAttrIds.Contains(oId.Value)) delAttrs.Add(id);
            }
        }

        foreach (var delId in delAttrs)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM ProductAttributes WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            attrsDeleted++;
        }

        // Strict delete of attribute values not in Odoo
        var delVals = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM ProductAttributeValues;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooValIds.Contains(oId.Value)) delVals.Add(id);
            }
        }

        foreach (var delId in delVals)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM ProductAttributeValues WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            valsDeleted++;
        }

        await transaction.CommitAsync();
        return (attrsImported, attrsUpdated, attrsDeleted, valsImported, valsUpdated, valsDeleted);
    }

    public async Task UpdateAttributeSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastAttributesSyncedAt = @syncedAt WHERE Id = 1;";
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
    // 4. PRODUCT TAGS REPOSITORY (STRICT 1:1 RECONCILIATION)
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
            result.Add(new ProductTagRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.GetInt32(1),
                Name = reader.GetString(2),
                ColorIndex = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                ProductsCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            });
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

            if (existingId != null && existingId != DBNull.Value)
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

        // Strict delete of tags not in Odoo
        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM ProductTags;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooTagIds.Contains(oId.Value)) delIds.Add(id);
            }
        }

        foreach (var delId in delIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM ProductTags WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateTagSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastTagsSyncedAt = @syncedAt WHERE Id = 1;";
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

    // =========================================================================
    // 5. PRODUCTS REPOSITORY (STRICT 1:1 RECONCILIATION)
    // =========================================================================
    public async Task<List<ProductRecord>> LoadAllProductsAsync()
    {
        var result = new List<ProductRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.Id, p.OdooId, p.Name, p.ProductType, p.ListPrice, p.StandardPrice, p.DefaultCode, p.Barcode,
                   p.PosCategoryId, p.PosCategoryName, p.VariantCount, p.ImageBase64, p.TagsJson, p.AttributesJson, p.ComboChoicesJson,
                   COALESCE(c.ColorHex, '#017E84')
            FROM Products p
            LEFT JOIN PosCategories c ON p.PosCategoryId = c.OdooId
            ORDER BY p.Name ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ProductRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Name = reader.GetString(2),
                ProductType = reader.IsDBNull(3) ? "consu" : reader.GetString(3),
                ListPrice = reader.IsDBNull(4) ? 0.00m : Convert.ToDecimal(reader.GetDouble(4)),
                StandardPrice = reader.IsDBNull(5) ? 0.00m : Convert.ToDecimal(reader.GetDouble(5)),
                DefaultCode = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Barcode = reader.IsDBNull(7) ? "" : reader.GetString(7),
                PosCategoryId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                PosCategoryName = reader.IsDBNull(9) ? null : reader.GetString(9),
                VariantCount = reader.IsDBNull(10) ? 1 : reader.GetInt32(10),
                ImageBase64 = reader.IsDBNull(11) ? null : reader.GetString(11),
                TagsJson = reader.IsDBNull(12) ? null : reader.GetString(12),
                AttributesJson = reader.IsDBNull(13) ? null : reader.GetString(13),
                ComboChoicesJson = reader.IsDBNull(14) ? null : reader.GetString(14),
                CategoryColorHex = reader.IsDBNull(15) ? "#017E84" : reader.GetString(15)
            });
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooProductsAsync(List<ProductDto> products)
    {
        int imported = 0, updated = 0, deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooProductIds = products.Select(p => p.OdooId).ToHashSet();

        foreach (var p in products)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM Products WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", p.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE Products
                    SET Name = @name,
                        ProductType = @type,
                        ListPrice = @listPrice,
                        StandardPrice = @stdPrice,
                        DefaultCode = @sku,
                        Barcode = @barcode,
                        PosCategoryId = @catId,
                        PosCategoryName = @catName,
                        VariantCount = @variants,
                        ImageBase64 = @image,
                        TagsJson = @tags,
                        AttributesJson = @attrs,
                        ComboChoicesJson = @combos,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@name", p.Name);
                updateCmd.Parameters.AddWithValue("@type", p.ProductType);
                updateCmd.Parameters.AddWithValue("@listPrice", (double)p.ListPrice);
                updateCmd.Parameters.AddWithValue("@stdPrice", (double)p.StandardPrice);
                updateCmd.Parameters.AddWithValue("@sku", p.DefaultCode);
                updateCmd.Parameters.AddWithValue("@barcode", p.Barcode);
                updateCmd.Parameters.AddWithValue("@catId", (object?)p.PosCategoryId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@catName", (object?)p.PosCategoryName ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@variants", p.VariantCount);
                updateCmd.Parameters.AddWithValue("@image", (object?)p.ImageBase64 ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@tags", (object?)p.TagsJson ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@attrs", (object?)p.AttributesJson ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@combos", (object?)p.ComboChoicesJson ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@odooId", p.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO Products (OdooId, Name, ProductType, ListPrice, StandardPrice, DefaultCode, Barcode,
                                         PosCategoryId, PosCategoryName, VariantCount, ImageBase64, TagsJson, AttributesJson, ComboChoicesJson)
                    VALUES (@odooId, @name, @type, @listPrice, @stdPrice, @sku, @barcode,
                            @catId, @catName, @variants, @image, @tags, @attrs, @combos);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", p.OdooId);
                insertCmd.Parameters.AddWithValue("@name", p.Name);
                insertCmd.Parameters.AddWithValue("@type", p.ProductType);
                insertCmd.Parameters.AddWithValue("@listPrice", (double)p.ListPrice);
                insertCmd.Parameters.AddWithValue("@stdPrice", (double)p.StandardPrice);
                insertCmd.Parameters.AddWithValue("@sku", p.DefaultCode);
                insertCmd.Parameters.AddWithValue("@barcode", p.Barcode);
                insertCmd.Parameters.AddWithValue("@catId", (object?)p.PosCategoryId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@catName", (object?)p.PosCategoryName ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@variants", p.VariantCount);
                insertCmd.Parameters.AddWithValue("@image", (object?)p.ImageBase64 ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@tags", (object?)p.TagsJson ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@attrs", (object?)p.AttributesJson ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@combos", (object?)p.ComboChoicesJson ?? DBNull.Value);
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        // Strict delete of any local product not in Odoo
        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM Products;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooProductIds.Contains(oId.Value)) delIds.Add(id);
            }
        }

        foreach (var delId in delIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM Products WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateProductSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastProductsSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastProductSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastProductsSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // 6. COMBO CHOICES REPOSITORY (STRICT 1:1 RECONCILIATION)
    // =========================================================================
    public async Task<List<ComboChoiceRecord>> LoadAllComboChoicesAsync()
    {
        var result = new List<ComboChoiceRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, OdooId, Name FROM ComboChoices ORDER BY Id ASC;";

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                result.Add(new ComboChoiceRecord
                {
                    Id = reader.GetInt64(0),
                    OdooId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    Name = reader.GetString(2)
                });
            }
        }

        foreach (var combo in result)
        {
            if (combo.OdooId.HasValue)
            {
                await using var lineCmd = connection.CreateCommand();
                lineCmd.CommandText = """
                    SELECT Id, OdooId, ComboId, ProductId, ProductName, OriginalPrice, ExtraPrice
                    FROM ComboChoiceLines
                    WHERE ComboId = @comboId
                    ORDER BY Id ASC;
                    """;
                lineCmd.Parameters.AddWithValue("@comboId", combo.OdooId.Value);

                await using var lineReader = await lineCmd.ExecuteReaderAsync();
                while (await lineReader.ReadAsync())
                {
                    combo.Lines.Add(new ComboChoiceLineRecord
                    {
                        Id = lineReader.GetInt64(0),
                        OdooId = lineReader.IsDBNull(1) ? null : lineReader.GetInt32(1),
                        ComboId = lineReader.IsDBNull(2) ? null : lineReader.GetInt32(2),
                        ProductId = lineReader.IsDBNull(3) ? null : lineReader.GetInt32(3),
                        ProductName = lineReader.GetString(4),
                        OriginalPrice = lineReader.IsDBNull(5) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(5)),
                        ExtraPrice = lineReader.IsDBNull(6) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(6))
                    });
                }
                combo.NotifyLinesChanged();
            }
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooCombosAsync(List<ComboChoiceDto> combos, List<ComboChoiceLineDto> lines)
    {
        int imported = 0, updated = 0, deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooComboIds = combos.Select(c => c.OdooId).ToHashSet();
        var odooLineIds = lines.Select(l => l.OdooId).ToHashSet();

        foreach (var c in combos)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ComboChoices WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", c.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = "UPDATE ComboChoices SET Name = @name, UpdatedAt = CURRENT_TIMESTAMP WHERE OdooId = @odooId;";
                updateCmd.Parameters.AddWithValue("@name", c.Name);
                updateCmd.Parameters.AddWithValue("@odooId", c.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = "INSERT INTO ComboChoices (OdooId, Name) VALUES (@odooId, @name);";
                insertCmd.Parameters.AddWithValue("@odooId", c.OdooId);
                insertCmd.Parameters.AddWithValue("@name", c.Name);
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        foreach (var l in lines)
        {
            await using var checkLine = connection.CreateCommand();
            checkLine.Transaction = transaction;
            checkLine.CommandText = "SELECT Id FROM ComboChoiceLines WHERE OdooId = @odooId LIMIT 1;";
            checkLine.Parameters.AddWithValue("@odooId", l.OdooId);
            var existingLineId = await checkLine.ExecuteScalarAsync();

            if (existingLineId != null && existingLineId != DBNull.Value)
            {
                await using var updateLine = connection.CreateCommand();
                updateLine.Transaction = transaction;
                updateLine.CommandText = """
                    UPDATE ComboChoiceLines
                    SET ComboId = @comboId, ProductId = @productId, ProductName = @productName,
                        OriginalPrice = @origPrice, ExtraPrice = @extraPrice, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateLine.Parameters.AddWithValue("@comboId", l.ComboId);
                updateLine.Parameters.AddWithValue("@productId", (object?)l.ProductId ?? DBNull.Value);
                updateLine.Parameters.AddWithValue("@productName", l.ProductName);
                updateLine.Parameters.AddWithValue("@origPrice", (double)l.OriginalPrice);
                updateLine.Parameters.AddWithValue("@extraPrice", (double)l.ExtraPrice);
                updateLine.Parameters.AddWithValue("@odooId", l.OdooId);
                await updateLine.ExecuteNonQueryAsync();
            }
            else
            {
                await using var insertLine = connection.CreateCommand();
                insertLine.Transaction = transaction;
                insertLine.CommandText = """
                    INSERT INTO ComboChoiceLines (OdooId, ComboId, ProductId, ProductName, OriginalPrice, ExtraPrice)
                    VALUES (@odooId, @comboId, @productId, @productName, @origPrice, @extraPrice);
                    """;
                insertLine.Parameters.AddWithValue("@odooId", l.OdooId);
                insertLine.Parameters.AddWithValue("@comboId", l.ComboId);
                insertLine.Parameters.AddWithValue("@productId", (object?)l.ProductId ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@productName", l.ProductName);
                insertLine.Parameters.AddWithValue("@origPrice", (double)l.OriginalPrice);
                insertLine.Parameters.AddWithValue("@extraPrice", (double)l.ExtraPrice);
                await insertLine.ExecuteNonQueryAsync();
            }
        }

        // Strict delete of combo lines not in Odoo
        var delLineIds = new List<long>();
        await using (var fetchLinesCmd = connection.CreateCommand())
        {
            fetchLinesCmd.Transaction = transaction;
            fetchLinesCmd.CommandText = "SELECT Id, OdooId FROM ComboChoiceLines;";
            await using var reader = await fetchLinesCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooLineIds.Contains(oId.Value)) delLineIds.Add(id);
            }
        }

        foreach (var delLineId in delLineIds)
        {
            await using var delLineCmd = connection.CreateCommand();
            delLineCmd.Transaction = transaction;
            delLineCmd.CommandText = "DELETE FROM ComboChoiceLines WHERE Id = @id;";
            delLineCmd.Parameters.AddWithValue("@id", delLineId);
            await delLineCmd.ExecuteNonQueryAsync();
        }

        // Strict delete of combos not in Odoo
        var delComboIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM ComboChoices;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooComboIds.Contains(oId.Value)) delComboIds.Add(id);
            }
        }

        foreach (var delId in delComboIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM ComboChoices WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateComboSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastCombosSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastComboSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastCombosSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // 7. POS SESSIONS REPOSITORY (STRICT 1:1 RECONCILIATION)
    // =========================================================================
    public async Task<List<PosSessionRecord>> LoadAllSessionsAsync()
    {
        var result = new List<PosSessionRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, Name, PosConfigId, PosConfigName, UserId, UserName, UserAvatarBase64,
                   StartAt, StopAt, State, OpeningBalance, ClosingBalance, OrdersCount, TotalPaymentsAmount, Employees
            FROM PosSessions
            ORDER BY COALESCE(OdooId, Id) DESC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PosSessionRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Name = reader.GetString(2),
                PosConfigId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                PosConfigName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                UserId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                UserName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                UserAvatarBase64 = reader.IsDBNull(7) ? null : reader.GetString(7),
                StartAt = reader.IsDBNull(8) ? null : (DateTime.TryParse(reader.GetString(8), out var st) ? st : null),
                StopAt = reader.IsDBNull(9) ? null : (DateTime.TryParse(reader.GetString(9), out var sp) ? sp : null),
                State = reader.IsDBNull(10) ? "opened" : reader.GetString(10),
                OpeningBalance = reader.IsDBNull(11) ? 0.00m : Convert.ToDecimal(reader.GetDouble(11)),
                ClosingBalance = reader.IsDBNull(12) ? 0.00m : Convert.ToDecimal(reader.GetDouble(12)),
                OrdersCount = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                TotalPaymentsAmount = reader.IsDBNull(14) ? 0.00m : Convert.ToDecimal(reader.GetDouble(14)),
                Employees = reader.IsDBNull(15) ? "" : reader.GetString(15)
            });
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooSessionsAsync(List<PosSessionDto> sessions)
    {
        int imported = 0, updated = 0, deleted = 0;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = sessions.Select(s => s.OdooId).ToHashSet();

        foreach (var s in sessions)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosSessions WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", s.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE PosSessions
                    SET Name = @name,
                        PosConfigId = @posConfigId,
                        PosConfigName = @posConfigName,
                        UserId = @userId,
                        UserName = @userName,
                        UserAvatarBase64 = @avatar,
                        StartAt = @startAt,
                        StopAt = @stopAt,
                        State = @state,
                        OpeningBalance = @openBal,
                        ClosingBalance = @closeBal,
                        OrdersCount = @ordersCount,
                        TotalPaymentsAmount = @totalPayments,
                        Employees = @employees,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@name", s.Name);
                updateCmd.Parameters.AddWithValue("@posConfigId", (object?)s.PosConfigId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@posConfigName", s.PosConfigName ?? "");
                updateCmd.Parameters.AddWithValue("@userId", (object?)s.UserId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@userName", s.UserName ?? "");
                updateCmd.Parameters.AddWithValue("@avatar", (object?)s.UserAvatarBase64 ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@startAt", s.StartAt.HasValue ? s.StartAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@stopAt", s.StopAt.HasValue ? s.StopAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@state", s.State);
                updateCmd.Parameters.AddWithValue("@openBal", (double)s.OpeningBalance);
                updateCmd.Parameters.AddWithValue("@closeBal", (double)s.ClosingBalance);
                updateCmd.Parameters.AddWithValue("@ordersCount", s.OrdersCount);
                updateCmd.Parameters.AddWithValue("@totalPayments", (double)s.TotalPaymentsAmount);
                updateCmd.Parameters.AddWithValue("@employees", s.Employees ?? "");
                updateCmd.Parameters.AddWithValue("@odooId", s.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO PosSessions (OdooId, Name, PosConfigId, PosConfigName, UserId, UserName, UserAvatarBase64,
                                            StartAt, StopAt, State, OpeningBalance, ClosingBalance, OrdersCount, TotalPaymentsAmount, Employees)
                    VALUES (@odooId, @name, @posConfigId, @posConfigName, @userId, @userName, @avatar,
                            @startAt, @stopAt, @state, @openBal, @closeBal, @ordersCount, @totalPayments, @employees);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", s.OdooId);
                insertCmd.Parameters.AddWithValue("@name", s.Name);
                insertCmd.Parameters.AddWithValue("@posConfigId", (object?)s.PosConfigId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@posConfigName", s.PosConfigName ?? "");
                insertCmd.Parameters.AddWithValue("@userId", (object?)s.UserId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@userName", s.UserName ?? "");
                insertCmd.Parameters.AddWithValue("@avatar", (object?)s.UserAvatarBase64 ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@startAt", s.StartAt.HasValue ? s.StartAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@stopAt", s.StopAt.HasValue ? s.StopAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@state", s.State);
                insertCmd.Parameters.AddWithValue("@openBal", (double)s.OpeningBalance);
                insertCmd.Parameters.AddWithValue("@closeBal", (double)s.ClosingBalance);
                insertCmd.Parameters.AddWithValue("@ordersCount", s.OrdersCount);
                insertCmd.Parameters.AddWithValue("@totalPayments", (double)s.TotalPaymentsAmount);
                insertCmd.Parameters.AddWithValue("@employees", s.Employees ?? "");
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        // Strict delete of any local session not in Odoo
        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM PosSessions;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooIds.Contains(oId.Value)) delIds.Add(id);
            }
        }

        foreach (var delId in delIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM PosSessions WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateSessionSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastSessionsSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastSessionSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastSessionsSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // 8. POS ORDERS & ORDER LINES REPOSITORY (STRICT 1:1 RECONCILIATION)
    // =========================================================================
    public async Task<List<PosOrderRecord>> LoadAllOrdersAsync(string? sessionFilter = null)
    {
        var result = new List<PosOrderRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        if (!string.IsNullOrWhiteSpace(sessionFilter))
        {
            cmd.CommandText = """
                SELECT Id, OdooId, Name, PosReference, TrackingNumber, SessionId, SessionOdooId, SessionName, PosConfigName,
                       DateOrder, CashierId, CashierName, CashierAvatarBase64, FiscalPosition, AmountTotal, AmountTax, AmountPaid,
                       Margin, MarginRatio, State, IsEdited, Company, Note
                FROM PosOrders
                WHERE SessionName = @filter OR PosConfigName = @filter OR CAST(SessionOdooId AS TEXT) = @filter
                ORDER BY COALESCE(OdooId, Id) DESC;
                """;
            cmd.Parameters.AddWithValue("@filter", sessionFilter.Trim());
        }
        else
        {
            cmd.CommandText = """
                SELECT Id, OdooId, Name, PosReference, TrackingNumber, SessionId, SessionOdooId, SessionName, PosConfigName,
                       DateOrder, CashierId, CashierName, CashierAvatarBase64, FiscalPosition, AmountTotal, AmountTax, AmountPaid,
                       Margin, MarginRatio, State, IsEdited, Company, Note
                FROM PosOrders
                ORDER BY COALESCE(OdooId, Id) DESC;
                """;
        }

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                result.Add(new PosOrderRecord
                {
                    Id = reader.GetInt64(0),
                    OdooId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    Name = reader.GetString(2),
                    PosReference = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    TrackingNumber = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    SessionId = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    SessionOdooId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    SessionName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    PosConfigName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    DateOrder = reader.IsDBNull(9) ? null : (DateTime.TryParse(reader.GetString(9), out var d) ? d : null),
                    CashierId = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    CashierName = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    CashierAvatarBase64 = reader.IsDBNull(12) ? null : reader.GetString(12),
                    FiscalPosition = reader.IsDBNull(13) ? "Standard Tax" : reader.GetString(13),
                    AmountTotal = reader.IsDBNull(14) ? 0.00m : Convert.ToDecimal(reader.GetDouble(14)),
                    AmountTax = reader.IsDBNull(15) ? 0.00m : Convert.ToDecimal(reader.GetDouble(15)),
                    AmountPaid = reader.IsDBNull(16) ? 0.00m : Convert.ToDecimal(reader.GetDouble(16)),
                    Margin = reader.IsDBNull(17) ? 0.00m : Convert.ToDecimal(reader.GetDouble(17)),
                    MarginRatio = reader.IsDBNull(18) ? 0.00m : Convert.ToDecimal(reader.GetDouble(18)),
                    State = reader.IsDBNull(19) ? "paid" : reader.GetString(19),
                    IsEdited = !reader.IsDBNull(20) && reader.GetInt32(20) == 1,
                    Company = reader.IsDBNull(21) ? "My Company" : reader.GetString(21),
                    Note = reader.IsDBNull(22) ? "" : reader.GetString(22)
                });
            }
        }

        foreach (var order in result)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.CommandText = """
                SELECT Id, OdooId, OrderId, OrderOdooId, ProductId, ProductName, IsEdited, Qty, PriceUnit, Discount, Taxes, PriceSubtotal, PriceSubtotalIncl
                FROM PosOrderLines
                WHERE (OrderOdooId IS NOT NULL AND OrderOdooId = @odooId) OR (OrderId IS NOT NULL AND OrderId = @orderId);
                """;
            lineCmd.Parameters.AddWithValue("@odooId", (object?)order.OdooId ?? DBNull.Value);
            lineCmd.Parameters.AddWithValue("@orderId", order.Id);

            await using var lineReader = await lineCmd.ExecuteReaderAsync();
            while (await lineReader.ReadAsync())
            {
                order.Lines.Add(new PosOrderLineRecord
                {
                    Id = lineReader.GetInt64(0),
                    OdooId = lineReader.IsDBNull(1) ? null : lineReader.GetInt32(1),
                    OrderId = lineReader.IsDBNull(2) ? order.Id : lineReader.GetInt64(2),
                    OrderOdooId = lineReader.IsDBNull(3) ? order.OdooId : lineReader.GetInt32(3),
                    ProductId = lineReader.IsDBNull(4) ? null : lineReader.GetInt32(4),
                    ProductName = lineReader.GetString(5),
                    IsEdited = !lineReader.IsDBNull(6) && lineReader.GetInt32(6) == 1,
                    Qty = lineReader.IsDBNull(7) ? 1.00m : Convert.ToDecimal(lineReader.GetDouble(7)),
                    PriceUnit = lineReader.IsDBNull(8) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(8)),
                    Discount = lineReader.IsDBNull(9) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(9)),
                    Taxes = lineReader.IsDBNull(10) ? "15%" : lineReader.GetString(10),
                    PriceSubtotal = lineReader.IsDBNull(11) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(11)),
                    PriceSubtotalIncl = lineReader.IsDBNull(12) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(12))
                });
            }

            await using var payCmd = connection.CreateCommand();
            payCmd.CommandText = """
                SELECT Id, OdooId, OrderId, OrderOdooId, OrderName, SessionId, SessionOdooId, SessionName, CashierName,
                       PaymentMethodId, PaymentMethodName, Amount, CurrencyName, ForeignCurrency, PaymentDate
                FROM PosPayments
                WHERE (OrderOdooId IS NOT NULL AND OrderOdooId = @odooId) OR (OrderId IS NOT NULL AND OrderId = @orderId) OR OrderName = @orderName;
                """;
            payCmd.Parameters.AddWithValue("@odooId", (object?)order.OdooId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@orderId", order.Id);
            payCmd.Parameters.AddWithValue("@orderName", order.Name);

            await using var payReader = await payCmd.ExecuteReaderAsync();
            while (await payReader.ReadAsync())
            {
                order.Payments.Add(new PosPaymentRecord
                {
                    Id = payReader.GetInt64(0),
                    OdooId = payReader.IsDBNull(1) ? null : payReader.GetInt32(1),
                    OrderId = payReader.IsDBNull(2) ? order.Id : payReader.GetInt64(2),
                    OrderOdooId = payReader.IsDBNull(3) ? order.OdooId : payReader.GetInt32(3),
                    OrderName = payReader.IsDBNull(4) ? order.Name : payReader.GetString(4),
                    SessionId = payReader.IsDBNull(5) ? null : payReader.GetInt64(5),
                    SessionOdooId = payReader.IsDBNull(6) ? null : payReader.GetInt32(6),
                    SessionName = payReader.IsDBNull(7) ? "" : payReader.GetString(7),
                    CashierName = payReader.IsDBNull(8) ? "" : payReader.GetString(8),
                    PaymentMethodId = payReader.IsDBNull(9) ? null : payReader.GetInt32(9),
                    PaymentMethodName = payReader.IsDBNull(10) ? "Cash" : payReader.GetString(10),
                    Amount = payReader.IsDBNull(11) ? 0.00m : Convert.ToDecimal(payReader.GetDouble(11)),
                    CurrencyName = payReader.IsDBNull(12) ? "MAD" : payReader.GetString(12),
                    ForeignCurrency = payReader.IsDBNull(13) ? "—" : payReader.GetString(13),
                    PaymentDate = payReader.IsDBNull(14) ? null : (DateTime.TryParse(payReader.GetString(14), out var pd) ? pd : null)
                });
            }
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooOrdersAsync(
        List<PosOrderDto> orders,
        List<PosOrderLineDto> lines,
        List<PosPaymentDto>? payments = null)
    {
        int imported = 0, updated = 0, deleted = 0;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooOrderIds = orders.Select(o => o.OdooId).ToHashSet();
        var odooLineIds = lines.Select(l => l.OdooId).ToHashSet();

        foreach (var o in orders)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosOrders WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", o.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE PosOrders
                    SET Name = @name,
                        PosReference = @refNum,
                        TrackingNumber = @trackNum,
                        SessionOdooId = @sessOdooId,
                        SessionName = @sessName,
                        PosConfigName = @cfgName,
                        DateOrder = @dateOrder,
                        CashierId = @cashierId,
                        CashierName = @cashierName,
                        CashierAvatarBase64 = @avatar,
                        FiscalPosition = @fiscal,
                        AmountTotal = @total,
                        AmountTax = @tax,
                        AmountPaid = @paid,
                        Margin = @margin,
                        MarginRatio = @marginRatio,
                        State = @state,
                        IsEdited = @isEdited,
                        Company = @company,
                        Note = @note,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@name", o.Name);
                updateCmd.Parameters.AddWithValue("@refNum", o.PosReference);
                updateCmd.Parameters.AddWithValue("@trackNum", o.TrackingNumber);
                updateCmd.Parameters.AddWithValue("@sessOdooId", (object?)o.SessionOdooId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@sessName", o.SessionName);
                updateCmd.Parameters.AddWithValue("@cfgName", o.PosConfigName);
                updateCmd.Parameters.AddWithValue("@dateOrder", o.DateOrder.HasValue ? o.DateOrder.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@cashierId", (object?)o.CashierId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@cashierName", o.CashierName);
                updateCmd.Parameters.AddWithValue("@avatar", (object?)o.CashierAvatarBase64 ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@fiscal", o.FiscalPosition);
                updateCmd.Parameters.AddWithValue("@total", (double)o.AmountTotal);
                updateCmd.Parameters.AddWithValue("@tax", (double)o.AmountTax);
                updateCmd.Parameters.AddWithValue("@paid", (double)o.AmountPaid);
                updateCmd.Parameters.AddWithValue("@margin", (double)o.Margin);
                updateCmd.Parameters.AddWithValue("@marginRatio", (double)o.MarginRatio);
                updateCmd.Parameters.AddWithValue("@state", o.State);
                updateCmd.Parameters.AddWithValue("@isEdited", o.IsEdited ? 1 : 0);
                updateCmd.Parameters.AddWithValue("@company", o.Company);
                updateCmd.Parameters.AddWithValue("@note", o.Note);
                updateCmd.Parameters.AddWithValue("@odooId", o.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO PosOrders (OdooId, Name, PosReference, TrackingNumber, SessionOdooId, SessionName, PosConfigName,
                                          DateOrder, CashierId, CashierName, CashierAvatarBase64, FiscalPosition, AmountTotal, AmountTax,
                                          AmountPaid, Margin, MarginRatio, State, IsEdited, Company, Note)
                    VALUES (@odooId, @name, @refNum, @trackNum, @sessOdooId, @sessName, @cfgName,
                            @dateOrder, @cashierId, @cashierName, @avatar, @fiscal, @total, @tax,
                            @paid, @margin, @marginRatio, @state, @isEdited, @company, @note);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", o.OdooId);
                insertCmd.Parameters.AddWithValue("@name", o.Name);
                insertCmd.Parameters.AddWithValue("@refNum", o.PosReference);
                insertCmd.Parameters.AddWithValue("@trackNum", o.TrackingNumber);
                insertCmd.Parameters.AddWithValue("@sessOdooId", (object?)o.SessionOdooId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@sessName", o.SessionName);
                insertCmd.Parameters.AddWithValue("@cfgName", o.PosConfigName);
                insertCmd.Parameters.AddWithValue("@dateOrder", o.DateOrder.HasValue ? o.DateOrder.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@cashierId", (object?)o.CashierId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@cashierName", o.CashierName);
                insertCmd.Parameters.AddWithValue("@avatar", (object?)o.CashierAvatarBase64 ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@fiscal", o.FiscalPosition);
                insertCmd.Parameters.AddWithValue("@total", (double)o.AmountTotal);
                insertCmd.Parameters.AddWithValue("@tax", (double)o.AmountTax);
                insertCmd.Parameters.AddWithValue("@paid", (double)o.AmountPaid);
                insertCmd.Parameters.AddWithValue("@margin", (double)o.Margin);
                insertCmd.Parameters.AddWithValue("@marginRatio", (double)o.MarginRatio);
                insertCmd.Parameters.AddWithValue("@state", o.State);
                insertCmd.Parameters.AddWithValue("@isEdited", o.IsEdited ? 1 : 0);
                insertCmd.Parameters.AddWithValue("@company", o.Company);
                insertCmd.Parameters.AddWithValue("@note", o.Note);
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        // Upsert Order Lines
        foreach (var l in lines)
        {
            await using var checkLine = connection.CreateCommand();
            checkLine.Transaction = transaction;
            checkLine.CommandText = "SELECT Id FROM PosOrderLines WHERE OdooId = @odooId LIMIT 1;";
            checkLine.Parameters.AddWithValue("@odooId", l.OdooId);
            var existingLine = await checkLine.ExecuteScalarAsync();

            if (existingLine != null && existingLine != DBNull.Value)
            {
                await using var updateLine = connection.CreateCommand();
                updateLine.Transaction = transaction;
                updateLine.CommandText = """
                    UPDATE PosOrderLines
                    SET OrderOdooId = @orderOdooId, ProductId = @prodId, ProductName = @prodName, IsEdited = @isEdited,
                        Qty = @qty, PriceUnit = @unitPrice, Discount = @disc, Taxes = @taxes,
                        PriceSubtotal = @subtotal, PriceSubtotalIncl = @subtotalIncl, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateLine.Parameters.AddWithValue("@orderOdooId", l.OrderOdooId);
                updateLine.Parameters.AddWithValue("@prodId", (object?)l.ProductId ?? DBNull.Value);
                updateLine.Parameters.AddWithValue("@prodName", l.ProductName);
                updateLine.Parameters.AddWithValue("@isEdited", l.IsEdited ? 1 : 0);
                updateLine.Parameters.AddWithValue("@qty", (double)l.Qty);
                updateLine.Parameters.AddWithValue("@unitPrice", (double)l.PriceUnit);
                updateLine.Parameters.AddWithValue("@disc", (double)l.Discount);
                updateLine.Parameters.AddWithValue("@taxes", l.Taxes);
                updateLine.Parameters.AddWithValue("@subtotal", (double)l.PriceSubtotal);
                updateLine.Parameters.AddWithValue("@subtotalIncl", (double)l.PriceSubtotalIncl);
                updateLine.Parameters.AddWithValue("@odooId", l.OdooId);
                await updateLine.ExecuteNonQueryAsync();
            }
            else
            {
                await using var insertLine = connection.CreateCommand();
                insertLine.Transaction = transaction;
                insertLine.CommandText = """
                    INSERT INTO PosOrderLines (OdooId, OrderOdooId, ProductId, ProductName, IsEdited, Qty, PriceUnit, Discount, Taxes, PriceSubtotal, PriceSubtotalIncl)
                    VALUES (@odooId, @orderOdooId, @prodId, @prodName, @isEdited, @qty, @unitPrice, @disc, @taxes, @subtotal, @subtotalIncl);
                    """;
                insertLine.Parameters.AddWithValue("@odooId", l.OdooId);
                insertLine.Parameters.AddWithValue("@orderOdooId", l.OrderOdooId);
                insertLine.Parameters.AddWithValue("@prodId", (object?)l.ProductId ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@prodName", l.ProductName);
                insertLine.Parameters.AddWithValue("@isEdited", l.IsEdited ? 1 : 0);
                insertLine.Parameters.AddWithValue("@qty", (double)l.Qty);
                insertLine.Parameters.AddWithValue("@unitPrice", (double)l.PriceUnit);
                insertLine.Parameters.AddWithValue("@disc", (double)l.Discount);
                insertLine.Parameters.AddWithValue("@taxes", l.Taxes);
                insertLine.Parameters.AddWithValue("@subtotal", (double)l.PriceSubtotal);
                insertLine.Parameters.AddWithValue("@subtotalIncl", (double)l.PriceSubtotalIncl);
                await insertLine.ExecuteNonQueryAsync();
            }
        }

        // Strict delete of order lines not in Odoo
        var delLineIds = new List<long>();
        await using (var fetchLinesCmd = connection.CreateCommand())
        {
            fetchLinesCmd.Transaction = transaction;
            fetchLinesCmd.CommandText = "SELECT Id, OdooId FROM PosOrderLines;";
            await using var reader = await fetchLinesCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooLineIds.Contains(oId.Value)) delLineIds.Add(id);
            }
        }

        foreach (var delLineId in delLineIds)
        {
            await using var delLineCmd = connection.CreateCommand();
            delLineCmd.Transaction = transaction;
            delLineCmd.CommandText = "DELETE FROM PosOrderLines WHERE Id = @id;";
            delLineCmd.Parameters.AddWithValue("@id", delLineId);
            await delLineCmd.ExecuteNonQueryAsync();
        }

        // Strict delete of orders not in Odoo
        var delOrderIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM PosOrders;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooOrderIds.Contains(oId.Value)) delOrderIds.Add(id);
            }
        }

        foreach (var delId in delOrderIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM PosOrders WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();

        if (payments != null)
        {
            await UpsertOdooPaymentsAsync(payments);
        }

        return (imported, updated, deleted);
    }

    public async Task UpdateOrderSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastOrdersSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastOrderSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastOrdersSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // 9. POS PAYMENTS REPOSITORY (STRICT 1:1 RECONCILIATION)
    // =========================================================================
    public async Task<List<PosPaymentRecord>> LoadAllPaymentsAsync(string? sessionFilter = null)
    {
        var result = new List<PosPaymentRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        if (!string.IsNullOrWhiteSpace(sessionFilter))
        {
            cmd.CommandText = """
                SELECT Id, OdooId, OrderId, OrderOdooId, OrderName, SessionId, SessionOdooId, SessionName, CashierName,
                       PaymentMethodId, PaymentMethodName, Amount, CurrencyName, ForeignCurrency, PaymentDate
                FROM PosPayments
                WHERE SessionName = @filter OR CAST(SessionOdooId AS TEXT) = @filter
                ORDER BY COALESCE(OdooId, Id) DESC;
                """;
            cmd.Parameters.AddWithValue("@filter", sessionFilter.Trim());
        }
        else
        {
            cmd.CommandText = """
                SELECT Id, OdooId, OrderId, OrderOdooId, OrderName, SessionId, SessionOdooId, SessionName, CashierName,
                       PaymentMethodId, PaymentMethodName, Amount, CurrencyName, ForeignCurrency, PaymentDate
                FROM PosPayments
                ORDER BY COALESCE(OdooId, Id) DESC;
                """;
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PosPaymentRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                OrderId = reader.IsDBNull(2) ? null : reader.GetInt64(2),
                OrderOdooId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                OrderName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                SessionId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                SessionOdooId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                SessionName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                CashierName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                PaymentMethodId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                PaymentMethodName = reader.IsDBNull(10) ? "Cash" : reader.GetString(10),
                Amount = reader.IsDBNull(11) ? 0.00m : Convert.ToDecimal(reader.GetDouble(11)),
                CurrencyName = reader.IsDBNull(12) ? "MAD" : reader.GetString(12),
                ForeignCurrency = reader.IsDBNull(13) ? "—" : reader.GetString(13),
                PaymentDate = reader.IsDBNull(14) ? null : (DateTime.TryParse(reader.GetString(14), out var dt) ? dt : null)
            });
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooPaymentsAsync(List<PosPaymentDto> payments)
    {
        int imported = 0, updated = 0, deleted = 0;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = payments.Select(p => p.OdooId).ToHashSet();

        foreach (var p in payments)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosPayments WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", p.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE PosPayments
                    SET OrderOdooId = @orderOdooId, OrderName = @orderName, SessionOdooId = @sessOdooId, SessionName = @sessName,
                        CashierName = @cashier, PaymentMethodId = @methodId, PaymentMethodName = @methodName, Amount = @amount,
                        CurrencyName = @curr, ForeignCurrency = @foreignCurr, PaymentDate = @date, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@orderOdooId", (object?)p.OrderOdooId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@orderName", p.OrderName);
                updateCmd.Parameters.AddWithValue("@sessOdooId", (object?)p.SessionOdooId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@sessName", p.SessionName);
                updateCmd.Parameters.AddWithValue("@cashier", p.CashierName);
                updateCmd.Parameters.AddWithValue("@methodId", (object?)p.PaymentMethodId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@methodName", p.PaymentMethodName);
                updateCmd.Parameters.AddWithValue("@amount", (double)p.Amount);
                updateCmd.Parameters.AddWithValue("@curr", p.CurrencyName);
                updateCmd.Parameters.AddWithValue("@foreignCurr", p.ForeignCurrency);
                updateCmd.Parameters.AddWithValue("@date", p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@odooId", p.OdooId);
                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO PosPayments (OdooId, OrderOdooId, OrderName, SessionOdooId, SessionName, CashierName,
                                            PaymentMethodId, PaymentMethodName, Amount, CurrencyName, ForeignCurrency, PaymentDate)
                    VALUES (@odooId, @orderOdooId, @orderName, @sessOdooId, @sessName, @cashier,
                            @methodId, @methodName, @amount, @curr, @foreignCurr, @date);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", p.OdooId);
                insertCmd.Parameters.AddWithValue("@orderOdooId", (object?)p.OrderOdooId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@orderName", p.OrderName);
                insertCmd.Parameters.AddWithValue("@sessOdooId", (object?)p.SessionOdooId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@sessName", p.SessionName);
                insertCmd.Parameters.AddWithValue("@cashier", p.CashierName);
                insertCmd.Parameters.AddWithValue("@methodId", (object?)p.PaymentMethodId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@methodName", p.PaymentMethodName);
                insertCmd.Parameters.AddWithValue("@amount", (double)p.Amount);
                insertCmd.Parameters.AddWithValue("@curr", p.CurrencyName);
                insertCmd.Parameters.AddWithValue("@foreignCurr", p.ForeignCurrency);
                insertCmd.Parameters.AddWithValue("@date", p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        // Strict delete of payments not in Odoo
        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM PosPayments;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooIds.Contains(oId.Value)) delIds.Add(id);
            }
        }

        foreach (var delId in delIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM PosPayments WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdatePaymentSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastPaymentsSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastPaymentSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastPaymentsSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // 10. POS CONFIGURATIONS (pos.config) REPOSITORY (STRICT 1:1 RECONCILIATION)
    // =========================================================================
    public async Task<List<PosConfigRecord>> LoadAllPosConfigsAsync()
    {
        var result = new List<PosConfigRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, Name, CurrentSessionId, CurrentSessionName, CurrentSessionState,
                   LastClosingDate, LastClosingBalance, OpeningBalance,
                   ActiveUserId, ActiveUserName, ActiveUserAvatar, RecentSalesPointsJson
            FROM PosConfigs
            ORDER BY COALESCE(OdooId, Id) ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PosConfigRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.GetInt32(1),
                Name = reader.GetString(2),
                CurrentSessionId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                CurrentSessionName = reader.IsDBNull(4) ? null : reader.GetString(4),
                CurrentSessionState = reader.IsDBNull(5) ? "closed" : reader.GetString(5),
                LastClosingDate = reader.IsDBNull(6) ? null : (DateTime.TryParse(reader.GetString(6), out var dt) ? dt : null),
                LastClosingBalance = reader.IsDBNull(7) ? 0.00m : Convert.ToDecimal(reader.GetDouble(7)),
                OpeningBalance = reader.IsDBNull(8) ? 0.00m : Convert.ToDecimal(reader.GetDouble(8)),
                ActiveUserId = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                ActiveUserName = reader.IsDBNull(10) ? null : reader.GetString(10),
                ActiveUserAvatarBase64 = reader.IsDBNull(11) ? null : reader.GetString(11),
                RecentSalesPointsJson = reader.IsDBNull(12) ? "[]" : reader.GetString(12)
            });
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooPosConfigsAsync(List<PosConfigDto> configs)
    {
        int imported = 0, updated = 0, deleted = 0;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = configs.Select(c => c.OdooId).ToHashSet();

        foreach (var c in configs)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosConfigs WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", c.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value)
            {
                await using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = """
                    UPDATE PosConfigs
                    SET Name = @name,
                        CurrentSessionId = @sessId,
                        CurrentSessionName = @sessName,
                        CurrentSessionState = @state,
                        LastClosingDate = @closingDate,
                        LastClosingBalance = @closingBal,
                        OpeningBalance = @openBal,
                        ActiveUserId = @userId,
                        ActiveUserName = @userName,
                        ActiveUserAvatar = @avatar,
                        RecentSalesPointsJson = @salesPoints,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updateCmd.Parameters.AddWithValue("@odooId", c.OdooId);
                updateCmd.Parameters.AddWithValue("@name", c.Name);
                updateCmd.Parameters.AddWithValue("@sessId", (object?)c.CurrentSessionId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@sessName", (object?)c.CurrentSessionName ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@state", c.CurrentSessionState ?? "closed");
                updateCmd.Parameters.AddWithValue("@closingDate", c.LastClosingDate.HasValue ? c.LastClosingDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                updateCmd.Parameters.AddWithValue("@closingBal", (double)c.LastClosingBalance);
                updateCmd.Parameters.AddWithValue("@openBal", (double)c.OpeningBalance);
                updateCmd.Parameters.AddWithValue("@userId", (object?)c.ActiveUserId ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@userName", (object?)c.ActiveUserName ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@avatar", (object?)c.ActiveUserAvatar ?? DBNull.Value);
                updateCmd.Parameters.AddWithValue("@salesPoints", c.RecentSalesPointsJson ?? "[]");
                await updateCmd.ExecuteNonQueryAsync();
                updated++;
            }
            else
            {
                await using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = """
                    INSERT INTO PosConfigs (OdooId, Name, CurrentSessionId, CurrentSessionName, CurrentSessionState,
                                           LastClosingDate, LastClosingBalance, OpeningBalance,
                                           ActiveUserId, ActiveUserName, ActiveUserAvatar, RecentSalesPointsJson)
                    VALUES (@odooId, @name, @sessId, @sessName, @state,
                            @closingDate, @closingBal, @openBal,
                            @userId, @userName, @avatar, @salesPoints);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", c.OdooId);
                insertCmd.Parameters.AddWithValue("@name", c.Name);
                insertCmd.Parameters.AddWithValue("@sessId", (object?)c.CurrentSessionId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@sessName", (object?)c.CurrentSessionName ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@state", c.CurrentSessionState ?? "closed");
                insertCmd.Parameters.AddWithValue("@closingDate", c.LastClosingDate.HasValue ? c.LastClosingDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                insertCmd.Parameters.AddWithValue("@closingBal", (double)c.LastClosingBalance);
                insertCmd.Parameters.AddWithValue("@openBal", (double)c.OpeningBalance);
                insertCmd.Parameters.AddWithValue("@userId", (object?)c.ActiveUserId ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@userName", (object?)c.ActiveUserName ?? DBNull.Value);
                updateOrInsertParams(insertCmd, c);
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        static void updateOrInsertParams(SqliteCommand cmd, PosConfigDto c)
        {
            cmd.Parameters.AddWithValue("@avatar", (object?)c.ActiveUserAvatar ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@salesPoints", c.RecentSalesPointsJson ?? "[]");
        }

        // Strict delete of POS configs not in Odoo
        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM PosConfigs;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooIds.Contains(oId.Value)) delIds.Add(id);
            }
        }

        foreach (var delId in delIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM PosConfigs WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdatePosConfigSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastPosConfigsSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastPosConfigSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastPosConfigsSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // POS REGISTER & SELLING WORKFLOW HELPER METHODS
    // =========================================================================
    public async Task UpdateSessionOpeningControlAsync(int configId, int sessionId, decimal openingBalance, string? openingNote)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmdSession = connection.CreateCommand();
        cmdSession.CommandText = """
            UPDATE PosSessions
            SET OpeningBalance = @openBal,
                State = 'opened',
                StartAt = COALESCE(StartAt, CURRENT_TIMESTAMP),
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE OdooId = @sessId OR Id = @sessId;
            """;
        cmdSession.Parameters.AddWithValue("@openBal", (double)openingBalance);
        cmdSession.Parameters.AddWithValue("@sessId", sessionId);
        await cmdSession.ExecuteNonQueryAsync();

        await using var cmdConfig = connection.CreateCommand();
        cmdConfig.CommandText = """
            UPDATE PosConfigs
            SET OpeningBalance = @openBal,
                CurrentSessionState = 'opened',
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE OdooId = @cfgId OR Id = @cfgId;
            """;
        cmdConfig.Parameters.AddWithValue("@openBal", (double)openingBalance);
        cmdConfig.Parameters.AddWithValue("@cfgId", configId);
        await cmdConfig.ExecuteNonQueryAsync();
    }

    public async Task CloseSessionAsync(int configId, int sessionId, decimal closingBalance, string? closingNote)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmdSession = connection.CreateCommand();
        cmdSession.CommandText = """
            UPDATE PosSessions
            SET ClosingBalance = @closeBal,
                StopAt = CURRENT_TIMESTAMP,
                State = 'closed',
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE OdooId = @sessId OR Id = @sessId;
            """;
        cmdSession.Parameters.AddWithValue("@closeBal", (double)closingBalance);
        cmdSession.Parameters.AddWithValue("@sessId", sessionId);
        await cmdSession.ExecuteNonQueryAsync();

        await using var cmdConfig = connection.CreateCommand();
        cmdConfig.CommandText = """
            UPDATE PosConfigs
            SET CurrentSessionState = 'closed',
                LastClosingBalance = @closeBal,
                LastClosingDate = CURRENT_TIMESTAMP,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE OdooId = @cfgId OR Id = @cfgId;
            """;
        cmdConfig.Parameters.AddWithValue("@closeBal", (double)closingBalance);
        cmdConfig.Parameters.AddWithValue("@cfgId", configId);
        await cmdConfig.ExecuteNonQueryAsync();
    }

    public async Task<int> GetSessionDraftOrdersCountAsync(int sessionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM PosOrders
            WHERE (SessionId = @sessId OR SessionOdooId = @sessId)
              AND LOWER(State) IN ('draft', 'ongoing', 'new');
            """;
        cmd.Parameters.AddWithValue("@sessId", sessionId);
        var res = await cmd.ExecuteScalarAsync();
        return res is long l ? (int)l : 0;
    }

    public async Task CancelAllSessionDraftOrdersAsync(int sessionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM PosOrderLines
            WHERE OrderId IN (
                SELECT Id FROM PosOrders
                WHERE (SessionId = @sessId OR SessionOdooId = @sessId)
                  AND LOWER(State) IN ('draft', 'ongoing', 'new')
            );
            DELETE FROM PosOrders
            WHERE (SessionId = @sessId OR SessionOdooId = @sessId)
              AND LOWER(State) IN ('draft', 'ongoing', 'new');
            """;
        cmd.Parameters.AddWithValue("@sessId", sessionId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteDraftOrderAsync(long orderId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM PosOrderLines WHERE OrderId = @orderId;
            DELETE FROM PosOrders WHERE Id = @orderId AND LOWER(State) IN ('draft', 'ongoing', 'new');
            """;
        cmd.Parameters.AddWithValue("@orderId", orderId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<long> SavePosOrderAsync(PosOrderRecord order, List<PosOrderLineRecord> lines, List<PosPaymentRecord> payments)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        long orderId = order.Id;
        if (orderId > 0)
        {
            await using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = """
                UPDATE PosOrders
                SET Name = @name, PosReference = @posRef, TrackingNumber = @trackNum,
                    SessionId = @sessId, SessionOdooId = @sessOdooId, SessionName = @sessName,
                    PosConfigName = @cfgName, DateOrder = @dateOrder,
                    CashierId = @cashierId, CashierName = @cashierName,
                    AmountTotal = @total, AmountTax = @tax, AmountPaid = @paid,
                    State = @state, Note = @note, UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @id;
                """;
            updateCmd.Parameters.AddWithValue("@id", orderId);
            updateCmd.Parameters.AddWithValue("@name", order.Name);
            updateCmd.Parameters.AddWithValue("@posRef", order.PosReference);
            updateCmd.Parameters.AddWithValue("@trackNum", order.TrackingNumber);
            updateCmd.Parameters.AddWithValue("@sessId", (object?)order.SessionId ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@sessOdooId", (object?)order.SessionOdooId ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@sessName", order.SessionName);
            updateCmd.Parameters.AddWithValue("@cfgName", order.PosConfigName);
            updateCmd.Parameters.AddWithValue("@dateOrder", order.DateOrder.HasValue ? order.DateOrder.Value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            updateCmd.Parameters.AddWithValue("@cashierId", (object?)order.CashierId ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@cashierName", order.CashierName);
            updateCmd.Parameters.AddWithValue("@total", (double)order.AmountTotal);
            updateCmd.Parameters.AddWithValue("@tax", (double)order.AmountTax);
            updateCmd.Parameters.AddWithValue("@paid", (double)order.AmountPaid);
            updateCmd.Parameters.AddWithValue("@state", order.State);
            updateCmd.Parameters.AddWithValue("@note", order.Note ?? "");
            await updateCmd.ExecuteNonQueryAsync();

            await using var clearLinesCmd = connection.CreateCommand();
            clearLinesCmd.Transaction = transaction;
            clearLinesCmd.CommandText = "DELETE FROM PosOrderLines WHERE OrderId = @orderId;";
            clearLinesCmd.Parameters.AddWithValue("@orderId", orderId);
            await clearLinesCmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO PosOrders (Name, PosReference, TrackingNumber, SessionId, SessionOdooId, SessionName, PosConfigName,
                                       DateOrder, CashierId, CashierName, FiscalPosition, AmountTotal, AmountTax, AmountPaid,
                                       Margin, MarginRatio, State, IsEdited, Company, Note)
                VALUES (@name, @posRef, @trackNum, @sessId, @sessOdooId, @sessName, @cfgName,
                        @dateOrder, @cashierId, @cashierName, @fiscal, @total, @tax, @paid,
                        @margin, @marginRatio, @state, @isEdited, @company, @note);
                SELECT last_insert_rowid();
                """;
            insertCmd.Parameters.AddWithValue("@name", order.Name);
            insertCmd.Parameters.AddWithValue("@posRef", order.PosReference);
            insertCmd.Parameters.AddWithValue("@trackNum", order.TrackingNumber);
            insertCmd.Parameters.AddWithValue("@sessId", (object?)order.SessionId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@sessOdooId", (object?)order.SessionOdooId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@sessName", order.SessionName);
            insertCmd.Parameters.AddWithValue("@cfgName", order.PosConfigName);
            insertCmd.Parameters.AddWithValue("@dateOrder", order.DateOrder.HasValue ? order.DateOrder.Value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            insertCmd.Parameters.AddWithValue("@cashierId", (object?)order.CashierId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@cashierName", order.CashierName);
            insertCmd.Parameters.AddWithValue("@fiscal", order.FiscalPosition ?? "Standard Tax");
            insertCmd.Parameters.AddWithValue("@total", (double)order.AmountTotal);
            insertCmd.Parameters.AddWithValue("@tax", (double)order.AmountTax);
            insertCmd.Parameters.AddWithValue("@paid", (double)order.AmountPaid);
            insertCmd.Parameters.AddWithValue("@margin", (double)order.Margin);
            insertCmd.Parameters.AddWithValue("@marginRatio", (double)order.MarginRatio);
            insertCmd.Parameters.AddWithValue("@state", order.State);
            insertCmd.Parameters.AddWithValue("@isEdited", order.IsEdited ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@company", order.Company);
            insertCmd.Parameters.AddWithValue("@note", order.Note ?? "");
            orderId = (long)(await insertCmd.ExecuteScalarAsync() ?? 0L);
            order.Id = orderId;
        }

        foreach (var l in lines)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                INSERT INTO PosOrderLines (OrderId, ProductId, ProductName, IsEdited, Qty, PriceUnit, Discount, Taxes, PriceSubtotal, PriceSubtotalIncl)
                VALUES (@orderId, @productId, @productName, @isEdited, @qty, @unitPrice, @discount, @taxes, @subtotal, @subtotalIncl);
                """;
            lineCmd.Parameters.AddWithValue("@orderId", orderId);
            lineCmd.Parameters.AddWithValue("@productId", (object?)l.ProductId ?? DBNull.Value);
            lineCmd.Parameters.AddWithValue("@productName", l.ProductName);
            lineCmd.Parameters.AddWithValue("@isEdited", l.IsEdited ? 1 : 0);
            lineCmd.Parameters.AddWithValue("@qty", (double)l.Qty);
            lineCmd.Parameters.AddWithValue("@unitPrice", (double)l.PriceUnit);
            lineCmd.Parameters.AddWithValue("@discount", (double)l.Discount);
            lineCmd.Parameters.AddWithValue("@taxes", l.Taxes ?? "15%");
            lineCmd.Parameters.AddWithValue("@subtotal", (double)l.PriceSubtotal);
            lineCmd.Parameters.AddWithValue("@subtotalIncl", (double)l.PriceSubtotalIncl);
            await lineCmd.ExecuteNonQueryAsync();
        }

        foreach (var p in payments)
        {
            await using var payCmd = connection.CreateCommand();
            payCmd.Transaction = transaction;
            payCmd.CommandText = """
                INSERT INTO PosPayments (OrderId, OrderName, SessionId, SessionOdooId, SessionName, CashierName, PaymentMethodName, Amount, CurrencyName, PaymentDate)
                VALUES (@orderId, @orderName, @sessId, @sessOdooId, @sessName, @cashier, @method, @amount, @curr, @date);
                """;
            payCmd.Parameters.AddWithValue("@orderId", orderId);
            payCmd.Parameters.AddWithValue("@orderName", order.Name);
            payCmd.Parameters.AddWithValue("@sessId", (object?)order.SessionId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@sessOdooId", (object?)order.SessionOdooId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@sessName", order.SessionName);
            payCmd.Parameters.AddWithValue("@cashier", order.CashierName);
            payCmd.Parameters.AddWithValue("@method", p.PaymentMethodName ?? "Cash");
            payCmd.Parameters.AddWithValue("@amount", (double)p.Amount);
            payCmd.Parameters.AddWithValue("@curr", p.CurrencyName ?? "MAD");
            payCmd.Parameters.AddWithValue("@date", p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            await payCmd.ExecuteNonQueryAsync();
        }

        if (order.State == "paid" && (order.SessionOdooId.HasValue || order.SessionId.HasValue))
        {
            int sId = order.SessionOdooId ?? order.SessionId!.Value;
            await using var updSessCmd = connection.CreateCommand();
            updSessCmd.Transaction = transaction;
            updSessCmd.CommandText = """
                UPDATE PosSessions
                SET OrdersCount = OrdersCount + 1,
                    TotalPaymentsAmount = TotalPaymentsAmount + @amt,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE OdooId = @sId OR Id = @sId;
                """;
            updSessCmd.Parameters.AddWithValue("@amt", (double)order.AmountTotal);
            updSessCmd.Parameters.AddWithValue("@sId", sId);
            await updSessCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        return orderId;
    }
}