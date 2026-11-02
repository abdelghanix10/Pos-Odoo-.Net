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
                CurrencyName TEXT DEFAULT 'USD ($)',
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

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastProductsSyncedAt TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { /* Column already exists */ }

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastCombosSyncedAt TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { /* Column already exists */ }

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastSessionsSyncedAt TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { /* Column already exists */ }

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastOrdersSyncedAt TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { /* Column already exists */ }

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastPaymentsSyncedAt TEXT;";
            alterCmd.ExecuteNonQuery();
        }
        catch { /* Column already exists */ }

        try
        {
            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE OdooConfig ADD COLUMN LastPosConfigsSyncedAt TEXT;";
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

        // Seed initial products if table is empty
        using var checkProducts = connection.CreateCommand();
        checkProducts.CommandText = "SELECT COUNT(*) FROM Products;";
        long productCount = (long)(checkProducts.ExecuteScalar() ?? 0L);
        if (productCount == 0)
        {
            SeedDefaultProducts(connection);
        }

        // Seed initial combo choices if table is empty
        using var checkCombos = connection.CreateCommand();
        checkCombos.CommandText = "SELECT COUNT(*) FROM ComboChoices;";
        long comboCount = (long)(checkCombos.ExecuteScalar() ?? 0L);
        if (comboCount == 0)
        {
            SeedDefaultCombos(connection);
        }

        // Seed initial sessions if table is empty
        using var checkSessions = connection.CreateCommand();
        checkSessions.CommandText = "SELECT COUNT(*) FROM PosSessions;";
        long sessionCount = (long)(checkSessions.ExecuteScalar() ?? 0L);
        if (sessionCount == 0)
        {
            SeedDefaultSessions(connection);
        }

        // Seed initial orders if table is empty
        using var checkOrders = connection.CreateCommand();
        checkOrders.CommandText = "SELECT COUNT(*) FROM PosOrders;";
        long orderCount = (long)(checkOrders.ExecuteScalar() ?? 0L);
        if (orderCount == 0)
        {
            SeedDefaultOrders(connection);
        }

        // Seed initial payments if table is empty
        using var checkPayments = connection.CreateCommand();
        checkPayments.CommandText = "SELECT COUNT(*) FROM PosPayments;";
        long paymentCount = (long)(checkPayments.ExecuteScalar() ?? 0L);
        if (paymentCount == 0)
        {
            SeedDefaultPayments(connection);
        }

        // Seed initial POS configurations if table is empty
        using var checkConfigs = connection.CreateCommand();
        checkConfigs.CommandText = "SELECT COUNT(*) FROM PosConfigs;";
        long posConfigCount = (long)(checkConfigs.ExecuteScalar() ?? 0L);
        if (posConfigCount == 0)
        {
            SeedDefaultPosConfigs(connection);
        }

        EnsureBakeryDemoProducts(connection);
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

    private static void SeedDefaultProducts(SqliteConnection connection)
    {
        var products = new (int odooId, string name, string type, decimal listPrice, decimal cost, string sku, string barcode, int? catId, string? catName, int variants, string? tags, string? attrs, string? combos)[]
        {
            (1, "Acoustic Bloc Screens", "consu", 295.00m, 180.00m, "DEMO-0008", "08472910384", 2, "Desks", 2, "[\"Bestseller\", \"Featured\"]", "[{\"AttributeName\":\"Color\",\"Values\":\"White, Black\"}]", null),
            (2, "Black embroidered t-shirt", "consu", 35.00m, 12.50m, "E-COM11", "09182371928", 4, "Upper body", 8, "[\"New Arrival\"]", "[{\"AttributeName\":\"Size\",\"Values\":\"S, M, L, XL\"},{\"AttributeName\":\"Color\",\"Values\":\"Black, White\"}]", null),
            (3, "Office Chair Black", "consu", 159.00m, 85.00m, "FURN-0012", "07182937461", 3, "Chairs", 1, "[\"Featured\"]", null, null),
            (4, "Large Meeting Table", "consu", 850.00m, 450.00m, "CONF-0001", "08291048291", 2, "Desks", 3, "[\"Bestseller\"]", "[{\"AttributeName\":\"Size\",\"Values\":\"Small, Medium, Large\"}]", null),
            (5, "Desk Pad", "consu", 19.99m, 8.00m, "MISC-0034", "01928471928", 1, "Misc", 1, "[\"Special Offer\"]", null, null),
            (6, "Lunch Burger Combo", "combo", 14.50m, 7.00m, "COMBO-001", "09988112233", 6, "Others", 1, "[\"Featured\"]", null, "[{\"ComboName\":\"Burger Choice\",\"ItemsCount\":\"4 items\"},{\"ComboName\":\"Drink Choice\",\"ItemsCount\":\"4 items\"},{\"ComboName\":\"Side Choice\",\"ItemsCount\":\"3 items\"}]"),
            (7, "Breakfast Pastry Combo", "combo", 8.50m, 3.50m, "COMBO-002", "09988112244", 7, "Breads", 1, "[\"Bestseller\"]", null, "[{\"ComboName\":\"Pastry Choice\",\"ItemsCount\":\"3 items\"},{\"ComboName\":\"Drink Choice\",\"ItemsCount\":\"4 items\"}]"),
            (8, "Croissant", "consu", 3.50m, 1.20m, "BAKE-0001", "06677889900", 8, "Pastries", 1, "[\"Organic\"]", null, null),
            (9, "Baguette", "consu", 2.80m, 0.90m, "BAKE-0002", "06677889911", 7, "Breads", 1, "[\"Organic\"]", null, null),
            (10, "Maintenance Service", "service", 75.00m, 30.00m, "SERV-0005", "05544332211", 1, "Misc", 1, null, null, null),
            (11, "Ergonomic Desk", "consu", 420.00m, 260.00m, "FURN-0055", "03322114455", 2, "Desks", 2, "[\"Featured\"]", "[{\"AttributeName\":\"Color\",\"Values\":\"White, Oak\"}]", null),
            (12, "Conference Chair", "consu", 110.00m, 60.00m, "FURN-0023", "04455667788", 3, "Chairs", 4, "[\"Bestseller\"]", "[{\"AttributeName\":\"Color\",\"Values\":\"Grey, Black, Blue, Red\"}]", null)
        };

        foreach (var p in products)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Products (OdooId, Name, ProductType, ListPrice, StandardPrice, DefaultCode, Barcode, PosCategoryId, PosCategoryName, VariantCount, TagsJson, AttributesJson, ComboChoicesJson)
                VALUES (@odooId, @name, @type, @listPrice, @stdPrice, @sku, @barcode, @catId, @catName, @variants, @tags, @attrs, @combos);
                """;
            cmd.Parameters.AddWithValue("@odooId", p.odooId);
            cmd.Parameters.AddWithValue("@name", p.name);
            cmd.Parameters.AddWithValue("@type", p.type);
            cmd.Parameters.AddWithValue("@listPrice", (double)p.listPrice);
            cmd.Parameters.AddWithValue("@stdPrice", (double)p.cost);
            cmd.Parameters.AddWithValue("@sku", p.sku);
            cmd.Parameters.AddWithValue("@barcode", p.barcode);
            cmd.Parameters.AddWithValue("@catId", (object?)p.catId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@catName", (object?)p.catName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@variants", p.variants);
            cmd.Parameters.AddWithValue("@tags", (object?)p.tags ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@attrs", (object?)p.attrs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@combos", (object?)p.combos ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultCombos(SqliteConnection connection)
    {
        var combos = new (int odooId, string name)[]
        {
            (1, "Burger Choice"),
            (2, "Drink Choice"),
            (3, "Side Choice"),
            (4, "Pastry Choice")
        };

        foreach (var c in combos)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO ComboChoices (OdooId, Name) VALUES (@odooId, @name);";
            cmd.Parameters.AddWithValue("@odooId", c.odooId);
            cmd.Parameters.AddWithValue("@name", c.name);
            cmd.ExecuteNonQuery();
        }

        var lines = new (int odooId, int comboId, int? productId, string productName, decimal origPrice, decimal extraPrice)[]
        {
            (1, 1, 101, "Classic Beef Burger", 12.00m, 0.00m),
            (2, 1, 102, "Double Cheeseburger", 14.00m, 2.00m),
            (3, 1, 103, "Veggie Burger", 11.50m, 0.00m),
            (4, 1, 104, "Crispy Chicken Burger", 13.00m, 1.00m),

            (5, 2, 201, "Soda Can 330ml", 2.50m, 0.00m),
            (6, 2, 202, "Mineral Water 500ml", 2.00m, 0.00m),
            (7, 2, 203, "Fresh Orange Juice", 4.50m, 1.50m),
            (8, 2, 204, "Iced Lemon Tea", 3.50m, 0.50m),

            (9, 3, 301, "French Fries", 3.50m, 0.00m),
            (10, 3, 302, "Onion Rings", 4.00m, 0.50m),
            (11, 3, 303, "Caesar Salad", 5.00m, 1.50m),

            (12, 4, 401, "Butter Croissant", 3.50m, 0.00m),
            (13, 4, 402, "Pain au Chocolat", 4.00m, 0.50m),
            (14, 4, 403, "Almond Danish", 4.50m, 1.00m)
        };

        foreach (var l in lines)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ComboChoiceLines (OdooId, ComboId, ProductId, ProductName, OriginalPrice, ExtraPrice)
                VALUES (@odooId, @comboId, @productId, @productName, @origPrice, @extraPrice);
                """;
            cmd.Parameters.AddWithValue("@odooId", l.odooId);
            cmd.Parameters.AddWithValue("@comboId", l.comboId);
            cmd.Parameters.AddWithValue("@productId", (object?)l.productId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@productName", l.productName);
            cmd.Parameters.AddWithValue("@origPrice", (double)l.origPrice);
            cmd.Parameters.AddWithValue("@extraPrice", (double)l.extraPrice);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultSessions(SqliteConnection connection)
    {
        var sessions = new (int odooId, string name, int configId, string configName, int userId, string userName, string startAt, string? stopAt, string state, decimal openBal, decimal closeBal, int ordersCount, decimal totalPayments, string employees)[]
        {
            (4, "Bakery Shop/00004", 3, "Bakery Shop", 3, "Mitchell Admin", "2026-10-31 08:30:00", null, "opened", 100.00m, 0.00m, 4, 248.50m, "Mitchell Admin, Marc Demo"),
            (3, "Bakery Shop/00003", 3, "Bakery Shop", 1, "Administrator", "2026-10-30 08:00:00", "2026-10-30 18:00:00", "closed", 100.00m, 684.20m, 12, 584.20m, "Administrator"),
            (2, "Furniture Shop/00002", 1, "Furniture Shop", 2, "Marc Demo", "2026-10-31 09:15:00", null, "opened", 150.00m, 0.00m, 3, 1295.00m, "Marc Demo"),
            (1, "Clothes Shop/00001", 2, "Clothes Shop", 3, "Mitchell Admin", "2026-10-29 10:00:00", "2026-10-29 20:00:00", "closed", 80.00m, 425.00m, 8, 345.00m, "Mitchell Admin")
        };

        foreach (var s in sessions)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO PosSessions (OdooId, Name, PosConfigId, PosConfigName, UserId, UserName, StartAt, StopAt, State, OpeningBalance, ClosingBalance, OrdersCount, TotalPaymentsAmount, Employees)
                VALUES (@odooId, @name, @cfgId, @cfgName, @userId, @userName, @startAt, @stopAt, @state, @openBal, @closeBal, @ordersCount, @totalPayments, @emp);
                """;
            cmd.Parameters.AddWithValue("@odooId", s.odooId);
            cmd.Parameters.AddWithValue("@name", s.name);
            cmd.Parameters.AddWithValue("@cfgId", s.configId);
            cmd.Parameters.AddWithValue("@cfgName", s.configName);
            cmd.Parameters.AddWithValue("@userId", s.userId);
            cmd.Parameters.AddWithValue("@userName", s.userName);
            cmd.Parameters.AddWithValue("@startAt", s.startAt);
            cmd.Parameters.AddWithValue("@stopAt", (object?)s.stopAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@state", s.state);
            cmd.Parameters.AddWithValue("@openBal", (double)s.openBal);
            cmd.Parameters.AddWithValue("@closeBal", (double)s.closeBal);
            cmd.Parameters.AddWithValue("@ordersCount", s.ordersCount);
            cmd.Parameters.AddWithValue("@totalPayments", (double)s.totalPayments);
            cmd.Parameters.AddWithValue("@emp", s.employees);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultOrders(SqliteConnection connection)
    {
        var orders = new (int odooId, string name, string refNum, string trackNum, int sessionOdooId, string sessionName, string configName, string dateOrder, string cashier, string fiscalPos, decimal total, decimal tax, decimal paid, decimal margin, decimal marginRatio, string state)[]
        {
            (101, "Bakery Shop - 000001", "Order 00001-002-0001", "00001", 4, "Bakery Shop/00004", "Bakery Shop", "2026-10-31 09:12:15", "Mitchell Admin", "Standard Tax", 24.50m, 3.20m, 24.50m, 11.20m, 0.457m, "paid"),
            (102, "Bakery Shop - 000002", "Order 00001-002-0002", "00002", 4, "Bakery Shop/00004", "Bakery Shop", "2026-10-31 10:45:00", "Mitchell Admin", "Standard Tax", 42.00m, 5.48m, 42.00m, 20.10m, 0.478m, "done"),
            (103, "Furniture Shop - 000001", "Order 00002-001-0001", "00001", 2, "Furniture Shop/00002", "Furniture Shop", "2026-10-31 11:20:00", "Marc Demo", "Standard Tax", 454.00m, 59.22m, 454.00m, 189.00m, 0.416m, "paid"),
            (104, "Bakery Shop - 000003", "Order 00001-002-0003", "00003", 4, "Bakery Shop/00004", "Bakery Shop", "2026-10-31 13:05:00", "Mitchell Admin", "Standard Tax", 18.00m, 2.35m, 18.00m, 8.50m, 0.472m, "cancel")
        };

        foreach (var o in orders)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO PosOrders (OdooId, Name, PosReference, TrackingNumber, SessionOdooId, SessionName, PosConfigName, DateOrder, CashierName, FiscalPosition, AmountTotal, AmountTax, AmountPaid, Margin, MarginRatio, State)
                VALUES (@odooId, @name, @refNum, @trackNum, @sessId, @sessName, @cfgName, @date, @cashier, @fiscal, @total, @tax, @paid, @margin, @marginRatio, @state);
                """;
            cmd.Parameters.AddWithValue("@odooId", o.odooId);
            cmd.Parameters.AddWithValue("@name", o.name);
            cmd.Parameters.AddWithValue("@refNum", o.refNum);
            cmd.Parameters.AddWithValue("@trackNum", o.trackNum);
            cmd.Parameters.AddWithValue("@sessId", o.sessionOdooId);
            cmd.Parameters.AddWithValue("@sessName", o.sessionName);
            cmd.Parameters.AddWithValue("@cfgName", o.configName);
            cmd.Parameters.AddWithValue("@date", o.dateOrder);
            cmd.Parameters.AddWithValue("@cashier", o.cashier);
            cmd.Parameters.AddWithValue("@fiscal", o.fiscalPos);
            cmd.Parameters.AddWithValue("@total", (double)o.total);
            cmd.Parameters.AddWithValue("@tax", (double)o.tax);
            cmd.Parameters.AddWithValue("@paid", (double)o.paid);
            cmd.Parameters.AddWithValue("@margin", (double)o.margin);
            cmd.Parameters.AddWithValue("@marginRatio", (double)o.marginRatio);
            cmd.Parameters.AddWithValue("@state", o.state);
            cmd.ExecuteNonQuery();
        }

        var lines = new (int odooId, int orderOdooId, int prodId, string prodName, decimal qty, decimal unitPrice, decimal discount, string taxes, decimal subtotal, decimal subtotalIncl)[]
        {
            (1001, 101, 7, "Breakfast Pastry Combo", 2m, 8.50m, 0m, "15%", 14.78m, 17.00m),
            (1002, 101, 8, "Croissant", 2m, 3.50m, 0m, "15%", 6.09m, 7.00m),
            (1003, 101, 6, "Mineral Water 500ml", 1m, 2.00m, 75m, "15%", 0.43m, 0.50m),
            (1004, 102, 6, "Lunch Burger Combo", 2m, 14.50m, 0m, "15%", 25.22m, 29.00m),
            (1005, 102, 1, "Acoustic Bloc Screens", 1m, 13.00m, 0m, "15%", 11.30m, 13.00m),
            (1006, 103, 11, "Ergonomic Desk", 1m, 420.00m, 0m, "15%", 365.22m, 420.00m),
            (1007, 103, 8, "Soda Can 330ml", 2m, 2.50m, 0m, "15%", 4.35m, 5.00m),
            (1008, 103, 5, "Desk Pad", 1m, 29.00m, 0m, "15%", 25.21m, 29.00m),
            (1009, 104, 8, "Croissant", 3m, 3.50m, 0m, "15%", 9.13m, 10.50m),
            (1010, 104, 7, "Breakfast Pastry Combo", 1m, 7.50m, 0m, "15%", 6.52m, 7.50m)
        };

        foreach (var l in lines)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO PosOrderLines (OdooId, OrderOdooId, ProductId, ProductName, Qty, PriceUnit, Discount, Taxes, PriceSubtotal, PriceSubtotalIncl)
                VALUES (@odooId, @orderOdooId, @prodId, @prodName, @qty, @unitPrice, @discount, @taxes, @subtotal, @subtotalIncl);
                """;
            cmd.Parameters.AddWithValue("@odooId", l.odooId);
            cmd.Parameters.AddWithValue("@orderOdooId", l.orderOdooId);
            cmd.Parameters.AddWithValue("@prodId", l.prodId);
            cmd.Parameters.AddWithValue("@prodName", l.prodName);
            cmd.Parameters.AddWithValue("@qty", (double)l.qty);
            cmd.Parameters.AddWithValue("@unitPrice", (double)l.unitPrice);
            cmd.Parameters.AddWithValue("@discount", (double)l.discount);
            cmd.Parameters.AddWithValue("@taxes", l.taxes);
            cmd.Parameters.AddWithValue("@subtotal", (double)l.subtotal);
            cmd.Parameters.AddWithValue("@subtotalIncl", (double)l.subtotalIncl);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultPayments(SqliteConnection connection)
    {
        var payments = new (int odooId, int orderOdooId, string orderName, int sessionOdooId, string sessionName, string cashier, string method, decimal amount, string date)[]
        {
            (201, 101, "Bakery Shop - 000001", 4, "Bakery Shop/00004", "Mitchell Admin", "Cash", 24.50m, "2026-10-31 09:12:15"),
            (202, 102, "Bakery Shop - 000002", 4, "Bakery Shop/00004", "Mitchell Admin", "Bank", 42.00m, "2026-10-31 10:45:00"),
            (203, 103, "Furniture Shop - 000001", 2, "Furniture Shop/00002", "Marc Demo", "Bank", 454.00m, "2026-10-31 11:20:00"),
            (204, 101, "Bakery Shop - 000001", 4, "Bakery Shop/00004", "Mitchell Admin", "Cash", 35.00m, "2026-10-31 14:10:00"),
            (205, 102, "Bakery Shop - 000002", 4, "Bakery Shop/00004", "Mitchell Admin", "Cash", 55.00m, "2026-10-31 14:35:00"),
            (206, 103, "Furniture Shop - 000001", 2, "Furniture Shop/00002", "Marc Demo", "Bank", 841.00m, "2026-10-31 15:50:00"),
            (207, 101, "Bakery Shop - 000001", 4, "Bakery Shop/00004", "Mitchell Admin", "Cash", 92.00m, "2026-10-31 16:15:00")
        };

        foreach (var p in payments)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO PosPayments (OdooId, OrderOdooId, OrderName, SessionOdooId, SessionName, CashierName, PaymentMethodName, Amount, PaymentDate)
                VALUES (@odooId, @orderOdooId, @orderName, @sessionOdooId, @sessionName, @cashier, @method, @amount, @date);
                """;
            cmd.Parameters.AddWithValue("@odooId", p.odooId);
            cmd.Parameters.AddWithValue("@orderOdooId", p.orderOdooId);
            cmd.Parameters.AddWithValue("@orderName", p.orderName);
            cmd.Parameters.AddWithValue("@sessionOdooId", p.sessionOdooId);
            cmd.Parameters.AddWithValue("@sessionName", p.sessionName);
            cmd.Parameters.AddWithValue("@cashier", p.cashier);
            cmd.Parameters.AddWithValue("@method", p.method);
            cmd.Parameters.AddWithValue("@amount", (double)p.amount);
            cmd.Parameters.AddWithValue("@date", p.date);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultPosConfigs(SqliteConnection connection)
    {
        var configs = new (int odooId, string name, int? sessId, string? sessName, string state, string? closingDate, decimal closingBal, decimal openBal, int? userId, string? userName, string salesPoints)[]
        {
            (1, "Furniture Shop", null, "Furniture Shop/00002", "closed", "2026-10-20 18:00:00", 51.49m, 0.00m, null, null, "[140.0, 310.0, 220.0, 480.0, 390.0]"),
            (2, "Clothes Shop", 5, "Clothes Shop/00005", "opening_control", null, 0.00m, 0.00m, 3, "Mitchell Admin", "[0.0, 0.0, 0.0, 0.0, 0.0]"),
            (3, "Bakery Shop", 4, "Bakery Shop/00004", "opened", "2026-10-27 08:30:00", 0.00m, 25.59m, 3, "Mitchell Admin", "[65.0, 140.0, 95.0, 210.0, 248.5]")
        };

        foreach (var c in configs)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO PosConfigs (OdooId, Name, CurrentSessionId, CurrentSessionName, CurrentSessionState, LastClosingDate, LastClosingBalance, OpeningBalance, ActiveUserId, ActiveUserName, RecentSalesPointsJson)
                VALUES (@odooId, @name, @sessId, @sessName, @state, @closingDate, @closingBal, @openBal, @userId, @userName, @salesPoints);
                """;
            cmd.Parameters.AddWithValue("@odooId", c.odooId);
            cmd.Parameters.AddWithValue("@name", c.name);
            cmd.Parameters.AddWithValue("@sessId", (object?)c.sessId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sessName", (object?)c.sessName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@state", c.state);
            cmd.Parameters.AddWithValue("@closingDate", (object?)c.closingDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@closingBal", (double)c.closingBal);
            cmd.Parameters.AddWithValue("@openBal", (double)c.openBal);
            cmd.Parameters.AddWithValue("@userId", (object?)c.userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@userName", (object?)c.userName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@salesPoints", c.salesPoints);
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

    // =========================================================================
    // PRODUCTS REPOSITORY & SYNC (product.template / product.product)
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
            ORDER BY p.Id ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var p = new ProductRecord
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
            };
            result.Add(p);
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

            if (existingId != null)
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
                        ImageBase64 = CASE WHEN @image IS NOT NULL THEN @image ELSE ImageBase64 END,
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

        // Reconcile / Delete products not in Odoo Online
        if (products.Count > 0)
        {
            var delIds = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM Products WHERE OdooId IS NOT NULL;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooProductIds.Contains(oId)) delIds.Add(oId);
                }
            }

            foreach (var delId in delIds)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM Products WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateProductSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE OdooConfig
            SET LastProductsSyncedAt = @syncedAt
            WHERE Id = 1;
            """;
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
    // COMBO CHOICES REPOSITORY & SYNC (pos.combo & pos.combo.line)
    // =========================================================================
    public async Task<List<ComboChoiceRecord>> LoadAllComboChoicesAsync()
    {
        var result = new List<ComboChoiceRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, OdooId, Name FROM ComboChoices ORDER BY Id ASC;";

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var combo = new ComboChoiceRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Name = reader.GetString(2)
            };
            result.Add(combo);
        }

        // Load lines for each combo
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
                    var line = new ComboChoiceLineRecord
                    {
                        Id = lineReader.GetInt64(0),
                        OdooId = lineReader.IsDBNull(1) ? null : lineReader.GetInt32(1),
                        ComboId = lineReader.IsDBNull(2) ? null : lineReader.GetInt32(2),
                        ProductId = lineReader.IsDBNull(3) ? null : lineReader.GetInt32(3),
                        ProductName = lineReader.GetString(4),
                        OriginalPrice = lineReader.IsDBNull(5) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(5)),
                        ExtraPrice = lineReader.IsDBNull(6) ? 0.00m : Convert.ToDecimal(lineReader.GetDouble(6))
                    };
                    combo.Lines.Add(line);
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

        foreach (var c in combos)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ComboChoices WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", c.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null)
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

        // Upsert combo lines
        foreach (var l in lines)
        {
            await using var checkLine = connection.CreateCommand();
            checkLine.Transaction = transaction;
            checkLine.CommandText = "SELECT Id FROM ComboChoiceLines WHERE OdooId = @odooId LIMIT 1;";
            checkLine.Parameters.AddWithValue("@odooId", l.OdooId);
            var existingLineId = await checkLine.ExecuteScalarAsync();

            if (existingLineId != null)
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

        // Reconcile deleted combos
        if (combos.Count > 0)
        {
            var delIds = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM ComboChoices WHERE OdooId IS NOT NULL;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooComboIds.Contains(oId)) delIds.Add(oId);
                }
            }

            foreach (var delId in delIds)
            {
                await using var delLines = connection.CreateCommand();
                delLines.Transaction = transaction;
                delLines.CommandText = "DELETE FROM ComboChoiceLines WHERE ComboId = @id;";
                delLines.Parameters.AddWithValue("@id", delId);
                await delLines.ExecuteNonQueryAsync();

                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM ComboChoices WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateComboSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE OdooConfig
            SET LastCombosSyncedAt = @syncedAt
            WHERE Id = 1;
            """;
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
    // POS SESSIONS REPOSITORY
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
            ORDER BY Id DESC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var s = new PosSessionRecord
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
            };
            result.Add(s);
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

            if (existingId != null)
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
                        UserAvatarBase64 = CASE WHEN @avatar IS NOT NULL THEN @avatar ELSE UserAvatarBase64 END,
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

        if (sessions.Count > 0)
        {
            var delIds = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM PosSessions WHERE OdooId IS NOT NULL;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooIds.Contains(oId)) delIds.Add(oId);
                }
            }

            foreach (var delId in delIds)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM PosSessions WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
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
    // POS ORDERS REPOSITORY
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
                ORDER BY Id DESC;
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
                ORDER BY Id DESC;
                """;
        }

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var o = new PosOrderRecord
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
                };
                result.Add(o);
            }
        }

        // Populate Order Lines for each order
        foreach (var order in result)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.CommandText = """
                SELECT Id, OdooId, OrderId, OrderOdooId, ProductId, ProductName, IsEdited, Qty, PriceUnit, Discount, Taxes, PriceSubtotal, PriceSubtotalIncl
                FROM PosOrderLines
                WHERE (OrderOdooId IS NOT NULL AND OrderOdooId = @odooId) OR OrderId = @orderId;
                """;
            lineCmd.Parameters.AddWithValue("@odooId", (object?)order.OdooId ?? DBNull.Value);
            lineCmd.Parameters.AddWithValue("@orderId", order.Id);

            await using var lineReader = await lineCmd.ExecuteReaderAsync();
            while (await lineReader.ReadAsync())
            {
                var line = new PosOrderLineRecord
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
                };
                order.Lines.Add(line);
            }

            // Populate Payments for this order
            await using var payCmd = connection.CreateCommand();
            payCmd.CommandText = """
                SELECT Id, OdooId, OrderId, OrderOdooId, OrderName, SessionId, SessionOdooId, SessionName, CashierName,
                       PaymentMethodId, PaymentMethodName, Amount, CurrencyName, ForeignCurrency, PaymentDate
                FROM PosPayments
                WHERE (OrderOdooId IS NOT NULL AND OrderOdooId = @odooId) OR OrderId = @orderId OR OrderName = @orderName;
                """;
            payCmd.Parameters.AddWithValue("@odooId", (object?)order.OdooId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@orderId", order.Id);
            payCmd.Parameters.AddWithValue("@orderName", order.Name);

            await using var payReader = await payCmd.ExecuteReaderAsync();
            while (await payReader.ReadAsync())
            {
                var pay = new PosPaymentRecord
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
                    CurrencyName = payReader.IsDBNull(12) ? "USD ($)" : payReader.GetString(12),
                    ForeignCurrency = payReader.IsDBNull(13) ? "—" : payReader.GetString(13),
                    PaymentDate = payReader.IsDBNull(14) ? null : (DateTime.TryParse(payReader.GetString(14), out var pd) ? pd : null)
                };
                order.Payments.Add(pay);
            }
        }

        return result;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooOrdersAsync(List<PosOrderDto> orders, List<PosOrderLineDto> lines, List<PosPaymentDto>? payments = null)
    {
        int imported = 0, updated = 0, deleted = 0;
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = orders.Select(o => o.OdooId).ToHashSet();

        foreach (var o in orders)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosOrders WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", o.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null)
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
                        CashierAvatarBase64 = CASE WHEN @avatar IS NOT NULL THEN @avatar ELSE CashierAvatarBase64 END,
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

            if (existingLine != null)
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

        // Upsert optional payments associated with orders
        if (payments != null && payments.Count > 0)
        {
            foreach (var p in payments)
            {
                await using var checkPay = connection.CreateCommand();
                checkPay.Transaction = transaction;
                checkPay.CommandText = "SELECT Id FROM PosPayments WHERE OdooId = @odooId LIMIT 1;";
                checkPay.Parameters.AddWithValue("@odooId", p.OdooId);
                var existingPay = await checkPay.ExecuteScalarAsync();

                if (existingPay != null)
                {
                    await using var updatePay = connection.CreateCommand();
                    updatePay.Transaction = transaction;
                    updatePay.CommandText = """
                        UPDATE PosPayments
                        SET OrderOdooId = @orderOdooId, OrderName = @orderName, SessionOdooId = @sessOdooId, SessionName = @sessName,
                            CashierName = @cashier, PaymentMethodName = @method, Amount = @amount, PaymentDate = @date, UpdatedAt = CURRENT_TIMESTAMP
                        WHERE OdooId = @odooId;
                        """;
                    updatePay.Parameters.AddWithValue("@orderOdooId", (object?)p.OrderOdooId ?? DBNull.Value);
                    updatePay.Parameters.AddWithValue("@orderName", p.OrderName);
                    updatePay.Parameters.AddWithValue("@sessOdooId", (object?)p.SessionOdooId ?? DBNull.Value);
                    updatePay.Parameters.AddWithValue("@sessName", p.SessionName);
                    updatePay.Parameters.AddWithValue("@cashier", p.CashierName);
                    updatePay.Parameters.AddWithValue("@method", p.PaymentMethodName);
                    updatePay.Parameters.AddWithValue("@amount", (double)p.Amount);
                    updatePay.Parameters.AddWithValue("@date", p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                    updatePay.Parameters.AddWithValue("@odooId", p.OdooId);
                    await updatePay.ExecuteNonQueryAsync();
                }
                else
                {
                    await using var insertPay = connection.CreateCommand();
                    insertPay.Transaction = transaction;
                    insertPay.CommandText = """
                        INSERT INTO PosPayments (OdooId, OrderOdooId, OrderName, SessionOdooId, SessionName, CashierName, PaymentMethodName, Amount, PaymentDate)
                        VALUES (@odooId, @orderOdooId, @orderName, @sessOdooId, @sessName, @cashier, @method, @amount, @date);
                        """;
                    insertPay.Parameters.AddWithValue("@odooId", p.OdooId);
                    insertPay.Parameters.AddWithValue("@orderOdooId", (object?)p.OrderOdooId ?? DBNull.Value);
                    insertPay.Parameters.AddWithValue("@orderName", p.OrderName);
                    insertPay.Parameters.AddWithValue("@sessOdooId", (object?)p.SessionOdooId ?? DBNull.Value);
                    insertPay.Parameters.AddWithValue("@sessName", p.SessionName);
                    insertPay.Parameters.AddWithValue("@cashier", p.CashierName);
                    insertPay.Parameters.AddWithValue("@method", p.PaymentMethodName);
                    insertPay.Parameters.AddWithValue("@amount", (double)p.Amount);
                    insertPay.Parameters.AddWithValue("@date", p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
                    await insertPay.ExecuteNonQueryAsync();
                }
            }
        }

        if (orders.Count > 0)
        {
            var delIds = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM PosOrders WHERE OdooId IS NOT NULL;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooIds.Contains(oId)) delIds.Add(oId);
                }
            }

            foreach (var delId in delIds)
            {
                await using var delLines = connection.CreateCommand();
                delLines.Transaction = transaction;
                delLines.CommandText = "DELETE FROM PosOrderLines WHERE OrderOdooId = @id;";
                delLines.Parameters.AddWithValue("@id", delId);
                await delLines.ExecuteNonQueryAsync();

                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM PosOrders WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
        }

        await transaction.CommitAsync();
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
    // POS PAYMENTS REPOSITORY
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
                ORDER BY Id DESC;
                """;
            cmd.Parameters.AddWithValue("@filter", sessionFilter.Trim());
        }
        else
        {
            cmd.CommandText = """
                SELECT Id, OdooId, OrderId, OrderOdooId, OrderName, SessionId, SessionOdooId, SessionName, CashierName,
                       PaymentMethodId, PaymentMethodName, Amount, CurrencyName, ForeignCurrency, PaymentDate
                FROM PosPayments
                ORDER BY Id DESC;
                """;
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var p = new PosPaymentRecord
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
                CurrencyName = reader.IsDBNull(12) ? "USD ($)" : reader.GetString(12),
                ForeignCurrency = reader.IsDBNull(13) ? "—" : reader.GetString(13),
                PaymentDate = reader.IsDBNull(14) ? null : (DateTime.TryParse(reader.GetString(14), out var dt) ? dt : null)
            };
            result.Add(p);
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

            if (existingId != null)
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

        if (payments.Count > 0)
        {
            var delIds = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM PosPayments WHERE OdooId IS NOT NULL;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooIds.Contains(oId)) delIds.Add(oId);
                }
            }

            foreach (var delId in delIds)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM PosPayments WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
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
    // POS CONFIGURATIONS (pos.config) REPOSITORY
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
            ORDER BY Id ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var rec = new PosConfigRecord
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
            };
            result.Add(rec);
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
                insertCmd.Parameters.AddWithValue("@avatar", (object?)c.ActiveUserAvatar ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@salesPoints", c.RecentSalesPointsJson ?? "[]");
                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

        if (configs.Count > 0)
        {
            var delIds = new List<int>();
            await using (var fetchCmd = connection.CreateCommand())
            {
                fetchCmd.Transaction = transaction;
                fetchCmd.CommandText = "SELECT OdooId FROM PosConfigs WHERE OdooId IS NOT NULL;";
                await using var reader = await fetchCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int oId = reader.GetInt32(0);
                    if (!odooIds.Contains(oId)) delIds.Add(oId);
                }
            }

            foreach (var delId in delIds)
            {
                await using var delCmd = connection.CreateCommand();
                delCmd.Transaction = transaction;
                delCmd.CommandText = "DELETE FROM PosConfigs WHERE OdooId = @id;";
                delCmd.Parameters.AddWithValue("@id", delId);
                await delCmd.ExecuteNonQueryAsync();
                deleted++;
            }
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
            payCmd.Parameters.AddWithValue("@curr", p.CurrencyName ?? "USD ($)");
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

    private static void EnsureBakeryDemoProducts(SqliteConnection connection)
    {
        using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM Products WHERE Name = 'Wholemeal loaf';";
        long exists = (long)(checkCmd.ExecuteScalar() ?? 0L);
        if (exists > 0) return;

        var bakeryProducts = new (int odooId, string name, string type, decimal listPrice, decimal cost, string sku, string barcode, int catId, string catName, string? tags)[]
        {
            (13, "Wholemeal loaf", "consu", 3.44m, 1.20m, "BAKE-0013", "07700000013", 7, "Breads", "[\"Organic\"]"),
            (14, "Flour", "consu", 2.10m, 0.80m, "BAKE-0014", "07700000014", 7, "Breads", null),
            (15, "Tiger white loaf", "consu", 3.20m, 1.10m, "BAKE-0015", "07700000015", 7, "Breads", null),
            (16, "Butter Croissant", "consu", 2.80m, 1.00m, "BAKE-0016", "07700000016", 8, "Pastries", "[\"Bestseller\"]"),
            (17, "Apple Pie", "consu", 4.50m, 1.80m, "BAKE-0017", "07700000017", 8, "Pastries", "[\"Featured\"]"),
            (18, "Cherry Pie", "consu", 4.75m, 1.90m, "BAKE-0018", "07700000018", 8, "Pastries", null),
            (19, "Sourdough Loaf", "consu", 4.20m, 1.50m, "BAKE-0019", "07700000019", 7, "Breads", "[\"Bestseller\"]"),
            (20, "Multigrain Bread", "consu", 3.80m, 1.40m, "BAKE-0020", "07700000020", 7, "Breads", "[\"Organic\"]"),
            (21, "Rye Bread", "consu", 3.60m, 1.30m, "BAKE-0021", "07700000021", 7, "Breads", null),
            (22, "Cinnamon Roll", "consu", 3.10m, 1.10m, "BAKE-0022", "07700000022", 8, "Pastries", "[\"Special Offer\"]"),
            (23, "Pain au Chocolat", "consu", 3.25m, 1.15m, "BAKE-0023", "07700000023", 8, "Pastries", "[\"Bestseller\"]"),
            (24, "Blueberry Muffin", "consu", 2.95m, 1.05m, "BAKE-0024", "07700000024", 8, "Pastries", null),
            (25, "Bagel", "consu", 2.20m, 0.75m, "BAKE-0025", "07700000025", 7, "Breads", null),
            (26, "Cheese Croissant", "consu", 3.50m, 1.30m, "BAKE-0026", "07700000026", 8, "Pastries", null),
            (27, "Pecan Pie", "consu", 5.20m, 2.10m, "BAKE-0027", "07700000027", 8, "Pastries", "[\"Featured\"]")
        };

        foreach (var p in bakeryProducts)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO Products (OdooId, Name, ProductType, ListPrice, StandardPrice, DefaultCode, Barcode, PosCategoryId, PosCategoryName, VariantCount, TagsJson)
                VALUES (@odooId, @name, @type, @listPrice, @stdPrice, @sku, @barcode, @catId, @catName, 1, @tags);
                """;
            cmd.Parameters.AddWithValue("@odooId", p.odooId);
            cmd.Parameters.AddWithValue("@name", p.name);
            cmd.Parameters.AddWithValue("@type", p.type);
            cmd.Parameters.AddWithValue("@listPrice", (double)p.listPrice);
            cmd.Parameters.AddWithValue("@stdPrice", (double)p.cost);
            cmd.Parameters.AddWithValue("@sku", p.sku);
            cmd.Parameters.AddWithValue("@barcode", p.barcode);
            cmd.Parameters.AddWithValue("@catId", p.catId);
            cmd.Parameters.AddWithValue("@catName", p.catName);
            cmd.Parameters.AddWithValue("@tags", (object?)p.tags ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }
}

