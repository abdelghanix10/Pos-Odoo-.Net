using System.Globalization;
using System.IO;
using System.Text.Json;
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
    string? AvatarBase64,
    string Role = "User",
    string Status = "Confirmed",
    string Language = "English (US)"
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
    string? ComboChoicesJson,
    string InternalCategoryName = "All",
    decimal QtyAvailable = 0.00m,
    bool SaleOk = true,
    bool PurchaseOk = true,
    bool AvailableInPos = true,
    bool TrackInventory = false,
    bool ToWeight = false,
    string SalesTaxLabel = "15%",
    decimal SalesTaxRate = 15.0m,
    string PurchaseTaxLabel = "15%",
    string UomName = "Units",
    string CompanyName = "",
    string PosDescription = ""
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
    string? RecentSalesPointsJson,
    string SelectedPaymentMethodsCsv = "Cash,Card,Customer Account",
    bool IsOneClickPaymentEnabled = false,
    string OneClickPaymentMethod = "Cash",
    bool IsRestrictCategoriesEnabled = false,
    string RestrictedCategoriesCsv = "",
    bool IsVariantsEnabled = true,
    bool IsAutomaticReceiptPrintingEnabled = false
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
    int PaymentsCount,
    int CompaniesCount = 0,
    int PaymentMethodsCount = 0,
    int PendingSyncCount = 0
);

// =========================================================================
// OFFLINE SEQUENTIAL SYNC QUEUE RECORDS & PAYLOADS
// =========================================================================
public record PendingSyncItemRecord(
    long Id,
    string OperationType,
    string EntityUuid,
    long? LocalEntityId,
    int? PosConfigOdooId,
    int? SessionOdooId,
    string PayloadJson,
    string Status,
    int RetryCount,
    string? LastError,
    string CreatedAt
);

public record OpenSessionSyncPayload(
    int ConfigOdooId,
    string ConfigName,
    long LocalSessionId,
    int? ExistingSessionOdooId,
    decimal OpeningBalance,
    string? OpeningNote,
    int? UserId,
    string CashierName,
    DateTime OpenedAt
);

public record PosOrderLineSyncPayload(
    int? ProductOdooId,
    string ProductName,
    decimal Qty,
    decimal PriceUnit,
    decimal Discount,
    string Taxes,
    decimal TaxRate,
    decimal PriceSubtotal,
    decimal PriceSubtotalIncl,
    string? CustomerNote
);

public record PosPaymentSyncPayload(
    int? PaymentMethodOdooId,
    string PaymentMethodName,
    decimal Amount,
    DateTime PaymentDate
);

public record CreateOrderSyncPayload(
    string OrderUuid,
    long LocalOrderId,
    int ConfigOdooId,
    int? SessionOdooId,
    long? LocalSessionId,
    string Name,
    string PosReference,
    string TrackingNumber,
    DateTime DateOrder,
    int? CashierId,
    string CashierName,
    int? PartnerId,
    string CustomerName,
    bool IsToInvoice,
    decimal AmountTotal,
    decimal AmountTax,
    decimal AmountPaid,
    string Note,
    List<PosOrderLineSyncPayload> Lines,
    List<PosPaymentSyncPayload> Payments
);

public record CashInOutSyncPayload(
    int ConfigOdooId,
    int? SessionOdooId,
    long? LocalSessionId,
    decimal Amount,
    string Reason,
    DateTime CreatedAt
);

public record CloseSessionSyncPayload(
    int ConfigOdooId,
    int? SessionOdooId,
    long? LocalSessionId,
    decimal ClosingCashCount,
    string? ClosingNote,
    DateTime ClosedAt,
    decimal OpeningBalance = 0m,
    DateTime? OpenedAt = null
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

            CREATE TABLE IF NOT EXISTS Companies (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                Street TEXT,
                Street2 TEXT,
                City TEXT,
                StateName TEXT,
                Zip TEXT,
                CountryName TEXT,
                Vat TEXT,
                Ice TEXT,
                CurrencyName TEXT DEFAULT 'MAD',
                Phone TEXT,
                Email TEXT,
                Website TEXT,
                EmailDomain TEXT,
                ColorIndex INTEGER DEFAULT 2,
                ColorHex TEXT DEFAULT '#D97736',
                ParentId INTEGER,
                ParentName TEXT,
                LogoBase64 TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
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
                Role TEXT DEFAULT 'User',
                Status TEXT DEFAULT 'Confirmed',
                Language TEXT DEFAULT 'English (US)',
                AvatarBase64 TEXT,
                IsVisibleInPos INTEGER DEFAULT 1,
                IsPurpleTheme INTEGER DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS PosPaymentMethods (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Name TEXT NOT NULL,
                MethodType TEXT DEFAULT 'cash',
                JournalId INTEGER,
                JournalName TEXT,
                CompanyId INTEGER,
                CompanyName TEXT,
                LinkedPosConfigs TEXT,
                Integration TEXT DEFAULT 'None required',
                ImageBase64 TEXT,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
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
                InternalCategoryName TEXT DEFAULT 'All',
                VariantCount INTEGER DEFAULT 1,
                QtyAvailable REAL DEFAULT 0.0,
                SaleOk INTEGER DEFAULT 1,
                PurchaseOk INTEGER DEFAULT 1,
                AvailableInPos INTEGER DEFAULT 1,
                TrackInventory INTEGER DEFAULT 0,
                ToWeight INTEGER DEFAULT 0,
                SalesTaxLabel TEXT DEFAULT '15%',
                SalesTaxRate REAL DEFAULT 15.0,
                PurchaseTaxLabel TEXT DEFAULT '15%',
                UomName TEXT DEFAULT 'Units',
                CompanyName TEXT DEFAULT '',
                PosDescription TEXT DEFAULT '',
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
                CashInOutTotal REAL DEFAULT 0.0,
                OpeningNote TEXT,
                ClosingNote TEXT,
                OrdersCount INTEGER DEFAULT 0,
                TotalPaymentsAmount REAL DEFAULT 0.0,
                Employees TEXT,
                IsSynced INTEGER DEFAULT 1,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PosOrders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OdooId INTEGER UNIQUE,
                Uuid TEXT,
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
                PartnerId INTEGER,
                CustomerName TEXT,
                IsToInvoice INTEGER DEFAULT 0,
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
                IsSynced INTEGER DEFAULT 1,
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
                CustomerNote TEXT,
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
                IsSynced INTEGER DEFAULT 1,
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
                SelectedPaymentMethodsCsv TEXT DEFAULT 'Cash,Card,Customer Account',
                IsOneClickPaymentEnabled INTEGER DEFAULT 0,
                OneClickPaymentMethodsCsv TEXT DEFAULT 'Cash',
                IsRestrictCategoriesEnabled INTEGER DEFAULT 0,
                RestrictedCategoriesCsv TEXT DEFAULT '',
                IsVariantsEnabled INTEGER DEFAULT 1,
                IsAutomaticReceiptPrintingEnabled INTEGER DEFAULT 0,
                IsPreventLineMergingEnabled INTEGER DEFAULT 0,
                IsPrintLastReceiptEnabled INTEGER DEFAULT 0,
                IsCustomOrderReceiptsEnabled INTEGER DEFAULT 0,
                CustomOrderReceiptDesignId INTEGER,
                IsHideOpeningCashControlEnabled INTEGER DEFAULT 0,
                DefaultOpeningCashAmount REAL DEFAULT 0.0,
                IsSecondReceiptEnabled INTEGER DEFAULT 0,
                SecondReceiptDesignId INTEGER,
                IsHideClosingRegisterEnabled INTEGER DEFAULT 0,
                CustomSalesReceiptDesignId INTEGER,
                IsHideReviewOrdersEnabled INTEGER DEFAULT 0,
                NumbersFirstOrProducts TEXT DEFAULT 'products',
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PosReceiptDesigns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                OpenCashDrawerOnly INTEGER DEFAULT 0,
                ReceiptXml TEXT DEFAULT '',
                CompanyId INTEGER,
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS GeneralSettings (
                Id INTEGER PRIMARY KEY,
                ScreenMode TEXT DEFAULT 'Window',
                DefaultCompanyId INTEGER,
                DefaultPrinterName TEXT DEFAULT '',
                PaperWidth TEXT DEFAULT '80mm',
                AutoPrintReceipt INTEGER DEFAULT 0,
                CashDrawerPulse INTEGER DEFAULT 0,
                PrintHeaderLogo INTEGER DEFAULT 1,
                PrinterEncoding TEXT DEFAULT 'UTF-8',
                UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE IF NOT EXISTS PendingSyncQueue (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OperationType TEXT NOT NULL,
                EntityUuid TEXT NOT NULL,
                LocalEntityId INTEGER,
                PosConfigOdooId INTEGER,
                SessionOdooId INTEGER,
                PayloadJson TEXT NOT NULL,
                Status TEXT DEFAULT 'Pending',
                RetryCount INTEGER DEFAULT 0,
                LastError TEXT,
                CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP
            );
            """;
        command.ExecuteNonQuery();

        string[] migrations =
        [
            "ALTER TABLE OdooConfig ADD COLUMN LastCategoriesSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastAttributesSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastTagsSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastProductsSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastCombosSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastSessionsSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastOrdersSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastPaymentsSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastPosConfigsSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastCompaniesSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN LastPaymentMethodsSyncedAt TEXT;",
            "ALTER TABLE OdooConfig ADD COLUMN HasPurgedLegacyDemoData INTEGER DEFAULT 0;",
            "ALTER TABLE Users ADD COLUMN Role TEXT DEFAULT 'User';",
            "ALTER TABLE Users ADD COLUMN Language TEXT DEFAULT 'English (US)';",
            "ALTER TABLE Products ADD COLUMN InternalCategoryName TEXT DEFAULT 'All';",
            "ALTER TABLE Products ADD COLUMN QtyAvailable REAL DEFAULT 0.0;",
            "ALTER TABLE Products ADD COLUMN SaleOk INTEGER DEFAULT 1;",
            "ALTER TABLE Products ADD COLUMN PurchaseOk INTEGER DEFAULT 1;",
            "ALTER TABLE Products ADD COLUMN AvailableInPos INTEGER DEFAULT 1;",
            "ALTER TABLE Products ADD COLUMN TrackInventory INTEGER DEFAULT 0;",
            "ALTER TABLE Products ADD COLUMN ToWeight INTEGER DEFAULT 0;",
            "ALTER TABLE Products ADD COLUMN SalesTaxLabel TEXT DEFAULT '15%';",
            "ALTER TABLE Products ADD COLUMN SalesTaxRate REAL DEFAULT 15.0;",
            "ALTER TABLE Products ADD COLUMN PurchaseTaxLabel TEXT DEFAULT '15%';",
            "ALTER TABLE Products ADD COLUMN UomName TEXT DEFAULT 'Units';",
            "ALTER TABLE Products ADD COLUMN CompanyName TEXT DEFAULT '';",
            "ALTER TABLE Products ADD COLUMN PosDescription TEXT DEFAULT '';",
            "ALTER TABLE PosSessions ADD COLUMN CashInOutTotal REAL DEFAULT 0.0;",
            "ALTER TABLE PosSessions ADD COLUMN OpeningNote TEXT;",
            "ALTER TABLE PosSessions ADD COLUMN ClosingNote TEXT;",
            "ALTER TABLE PosSessions ADD COLUMN IsSynced INTEGER DEFAULT 1;",
            "ALTER TABLE PosOrders ADD COLUMN Uuid TEXT;",
            "ALTER TABLE PosOrders ADD COLUMN PartnerId INTEGER;",
            "ALTER TABLE PosOrders ADD COLUMN CustomerName TEXT;",
            "ALTER TABLE PosOrders ADD COLUMN IsToInvoice INTEGER DEFAULT 0;",
            "ALTER TABLE PosOrders ADD COLUMN IsSynced INTEGER DEFAULT 1;",
            "ALTER TABLE PosOrderLines ADD COLUMN CustomerNote TEXT;",
            "ALTER TABLE PosPayments ADD COLUMN IsSynced INTEGER DEFAULT 1;",
            "ALTER TABLE PosConfigs ADD COLUMN SelectedPaymentMethodsCsv TEXT DEFAULT 'Cash,Card,Customer Account';",
            "ALTER TABLE PosConfigs ADD COLUMN IsOneClickPaymentEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN OneClickPaymentMethodsCsv TEXT DEFAULT 'Cash';",
            "ALTER TABLE PosConfigs ADD COLUMN IsRestrictCategoriesEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN RestrictedCategoriesCsv TEXT DEFAULT '';",
            "ALTER TABLE PosConfigs ADD COLUMN IsVariantsEnabled INTEGER DEFAULT 1;",
            "ALTER TABLE PosConfigs ADD COLUMN IsAutomaticReceiptPrintingEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN IsPreventLineMergingEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN IsPrintLastReceiptEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN IsCustomOrderReceiptsEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN CustomOrderReceiptDesignId INTEGER;",
            "ALTER TABLE PosConfigs ADD COLUMN IsHideOpeningCashControlEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN DefaultOpeningCashAmount REAL DEFAULT 0.0;",
            "ALTER TABLE PosConfigs ADD COLUMN IsSecondReceiptEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN SecondReceiptDesignId INTEGER;",
            "ALTER TABLE PosConfigs ADD COLUMN IsHideClosingRegisterEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN CustomSalesReceiptDesignId INTEGER;",
            "ALTER TABLE PosConfigs ADD COLUMN IsHideReviewOrdersEnabled INTEGER DEFAULT 0;",
            "ALTER TABLE PosConfigs ADD COLUMN NumbersFirstOrProducts TEXT DEFAULT 'products';",
            "ALTER TABLE PosReceiptDesigns ADD COLUMN CompanyId INTEGER;"
        ];

        foreach (string sql in migrations)
        {
            try
            {
                using var alterCmd = connection.CreateCommand();
                alterCmd.CommandText = sql;
                alterCmd.ExecuteNonQuery();
            }
            catch { }
        }

        // Seed default Receipt Designs if table is empty
        using (var checkDesignsCmd = connection.CreateCommand())
        {
            checkDesignsCmd.CommandText = "SELECT COUNT(*) FROM PosReceiptDesigns;";
            long designsCount = Convert.ToInt64(checkDesignsCmd.ExecuteScalar() ?? 0L);
            if (designsCount == 0)
            {
                using var seedDesignCmd = connection.CreateCommand();
                seedDesignCmd.CommandText = """
                    INSERT INTO PosReceiptDesigns (Name, OpenCashDrawerOnly, ReceiptXml)
                    VALUES
                        ('Standard Order Receipt', 0, @stdXml),
                        ('Simplified Kitchen / Second Receipt', 0, @secXml),
                        ('Open Cash Drawer Pulse Only', 1, '<receipt open-cashdrawer="true" />');
                    """;
                seedDesignCmd.Parameters.AddWithValue("@stdXml", ReceiptDesignRecord.GetDefaultXmlTemplate());
                seedDesignCmd.Parameters.AddWithValue("@secXml", """
<t t-name="point_of_sale.SecondReceipt">
    <div class="pos-receipt">
        <div class="pos-receipt-order-data">
            <h2><t t-esc="receipt.name" /></h2>
            <div>Date: <t t-esc="receipt.date.localestring" /></div>
        </div>
        <br />
        <div class="orderlines">
            <t t-foreach="receipt.orderlines" t-as="line" t-key="line.id">
                <div><t t-esc="line.qty" /> x <t t-esc="line.product_name" /></div>
            </t>
        </div>
    </div>
</t>
""");
                seedDesignCmd.ExecuteNonQuery();
            }
        }

        // Ensure Pastry Receipt, Cafe Receipt, and Sales Details Report exist in PosReceiptDesigns
        void EnsureDesignSeeded(string name, string xml)
        {
            try
            {
                using var chkCmd = connection.CreateCommand();
                chkCmd.CommandText = "SELECT COUNT(*) FROM PosReceiptDesigns WHERE LOWER(Name) = LOWER(@name);";
                chkCmd.Parameters.AddWithValue("@name", name);
                long exists = Convert.ToInt64(chkCmd.ExecuteScalar() ?? 0L);
                if (exists == 0)
                {
                    using var insCmd = connection.CreateCommand();
                    insCmd.CommandText = "INSERT INTO PosReceiptDesigns (Name, OpenCashDrawerOnly, ReceiptXml) VALUES (@name, 0, @xml);";
                    insCmd.Parameters.AddWithValue("@name", name);
                    insCmd.Parameters.AddWithValue("@xml", xml);
                    insCmd.ExecuteNonQuery();
                }
            }
            catch { }
        }

        EnsureDesignSeeded(ReceiptTemplateService.PastryReceiptName, ReceiptTemplateService.GetPastryDefaultXml());
        EnsureDesignSeeded(ReceiptTemplateService.CafeReceiptName, ReceiptTemplateService.GetCafeDefaultXml());
        EnsureDesignSeeded(ReceiptTemplateService.SaleDetailsReportName, ReceiptTemplateService.GetSaleDetailsReportDefaultXml());

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
            DELETE FROM PendingSyncQueue;
            DELETE FROM Companies;
            DELETE FROM Users;
            DELETE FROM PosPaymentMethods;
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
                LastPosConfigsSyncedAt = NULL,
                LastCompaniesSyncedAt = NULL,
                LastPaymentMethodsSyncedAt = NULL
            WHERE Id = 1;
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task ClearAllLocalDataAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        PurgeAllEntityTablesInternal(connection);
    }

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

        await using var queueCmd = connection.CreateCommand();
        queueCmd.CommandText = "SELECT COUNT(*) FROM PendingSyncQueue WHERE Status != 'Completed';";
        int pendingSync = Convert.ToInt32(await queueCmd.ExecuteScalarAsync() ?? 0);

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
            await CountTableAsync("PosPayments"),
            await CountTableAsync("Companies"),
            await CountTableAsync("PosPaymentMethods"),
            pendingSync
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
    // 1. COMPANIES REPOSITORY (res.company)
    // =========================================================================
    public async Task<List<CompanyRecord>> LoadAllCompaniesAsync()
    {
        var list = new List<CompanyRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, Name, Street, Street2, City, StateName, Zip, CountryName,
                   Vat, Ice, CurrencyName, Phone, Email, Website, EmailDomain,
                   ColorIndex, ColorHex, ParentId, ParentName, LogoBase64
            FROM Companies
            ORDER BY COALESCE(OdooId, Id) ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new CompanyRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                Name = reader.GetString(2),
                Street = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Street2 = reader.IsDBNull(4) ? "" : reader.GetString(4),
                City = reader.IsDBNull(5) ? "" : reader.GetString(5),
                StateName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                Zip = reader.IsDBNull(7) ? "" : reader.GetString(7),
                CountryName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Vat = reader.IsDBNull(9) ? "" : reader.GetString(9),
                Ice = reader.IsDBNull(10) ? "" : reader.GetString(10),
                CurrencyName = reader.IsDBNull(11) ? "MAD" : reader.GetString(11),
                Phone = reader.IsDBNull(12) ? "" : reader.GetString(12),
                Email = reader.IsDBNull(13) ? "" : reader.GetString(13),
                Website = reader.IsDBNull(14) ? "" : reader.GetString(14),
                EmailDomain = reader.IsDBNull(15) ? "" : reader.GetString(15),
                ColorIndex = reader.IsDBNull(16) ? 2 : reader.GetInt32(16),
                ColorHex = reader.IsDBNull(17) ? "#D97736" : reader.GetString(17),
                ParentId = reader.IsDBNull(18) ? null : reader.GetInt32(18),
                ParentName = reader.IsDBNull(19) ? "" : reader.GetString(19),
                LogoBase64 = reader.IsDBNull(20) ? null : reader.GetString(20)
            });
        }

        var byOdooId = list.Where(c => c.OdooId > 0).ToDictionary(c => c.OdooId, c => c);
        foreach (var comp in list)
        {
            if (comp.ParentId.HasValue && byOdooId.TryGetValue(comp.ParentId.Value, out var parent))
            {
                parent.Branches.Add(comp);
            }
        }

        return list;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooCompaniesAsync(List<OdooCompanyDto> companies)
    {
        int imported = 0, updated = 0, deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = companies.Select(c => c.OdooId).ToHashSet();

        foreach (var c in companies)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM Companies WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", c.OdooId);
            var existingObj = await checkCmd.ExecuteScalarAsync();

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingObj != null && existingObj != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE Companies
                    SET Name = @name, Street = @street, Street2 = @street2, City = @city,
                        StateName = @state, Zip = @zip, CountryName = @country,
                        Vat = @vat, Ice = @ice, CurrencyName = @currency,
                        Phone = @phone, Email = @email, Website = @website,
                        EmailDomain = @domain, ColorIndex = @colorIdx, ColorHex = @colorHex,
                        ParentId = @parentId, ParentName = @parentName, LogoBase64 = @logo,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO Companies (OdooId, Name, Street, Street2, City, StateName, Zip, CountryName,
                                           Vat, Ice, CurrencyName, Phone, Email, Website, EmailDomain,
                                           ColorIndex, ColorHex, ParentId, ParentName, LogoBase64)
                    VALUES (@odooId, @name, @street, @street2, @city, @state, @zip, @country,
                            @vat, @ice, @currency, @phone, @email, @website, @domain,
                            @colorIdx, @colorHex, @parentId, @parentName, @logo);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", c.OdooId);
            cmd.Parameters.AddWithValue("@name", c.Name);
            cmd.Parameters.AddWithValue("@street", c.Street ?? "");
            cmd.Parameters.AddWithValue("@street2", c.Street2 ?? "");
            cmd.Parameters.AddWithValue("@city", c.City ?? "");
            cmd.Parameters.AddWithValue("@state", c.StateName ?? "");
            cmd.Parameters.AddWithValue("@zip", c.Zip ?? "");
            cmd.Parameters.AddWithValue("@country", c.CountryName ?? "");
            cmd.Parameters.AddWithValue("@vat", c.Vat ?? "");
            cmd.Parameters.AddWithValue("@ice", c.Ice ?? "");
            cmd.Parameters.AddWithValue("@currency", c.CurrencyName ?? "MAD");
            cmd.Parameters.AddWithValue("@phone", c.Phone ?? "");
            cmd.Parameters.AddWithValue("@email", c.Email ?? "");
            cmd.Parameters.AddWithValue("@website", c.Website ?? "");
            cmd.Parameters.AddWithValue("@domain", c.EmailDomain ?? "");
            cmd.Parameters.AddWithValue("@colorIdx", c.ColorIndex);
            cmd.Parameters.AddWithValue("@colorHex", c.ColorHex ?? "#D97736");
            cmd.Parameters.AddWithValue("@parentId", (object?)c.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parentName", c.ParentName ?? "");
            cmd.Parameters.AddWithValue("@logo", (object?)c.LogoBase64 ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM Companies;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooIds.Contains(oId.Value)) delIds.Add(id);
            }
        }

        foreach (long delId in delIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM Companies WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdateCompanySyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastCompaniesSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastCompanySyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastCompaniesSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // 2. USERS REPOSITORY (res.users)
    // =========================================================================
    public async Task<List<PosUserRecord>> LoadAllUsersAsync()
    {
        var users = new List<PosUserRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OdooId, Name, Login, Email, Phone, Password, Pin, Status, AvatarBase64, IsVisibleInPos, IsPurpleTheme,
                   COALESCE(Role, 'User'), COALESCE(Language, 'English (US)')
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
            string role = reader.IsDBNull(11) ? "User" : reader.GetString(11);
            string language = reader.IsDBNull(12) ? "English (US)" : reader.GetString(12);

            users.Add(new PosUserRecord
            {
                OdooId = odooId,
                Name = name,
                Login = login,
                Email = email,
                Phone = phone,
                Password = password,
                Pin = pin,
                Role = role,
                Status = status,
                Language = language,
                AvatarBase64 = avatarBase64,
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
            INSERT INTO Users (OdooId, Name, Login, Email, Phone, Password, Pin, Role, Status, Language, AvatarBase64, IsVisibleInPos, IsPurpleTheme)
            VALUES (@odooId, @name, @login, @email, @phone, @pass, @pin, @role, @status, @lang, @avatar, @visible, @theme)
            ON CONFLICT(Login) DO UPDATE SET
                Name = excluded.Name,
                Email = excluded.Email,
                Phone = excluded.Phone,
                Password = excluded.Password,
                Pin = excluded.Pin,
                Role = excluded.Role,
                Status = excluded.Status,
                Language = excluded.Language,
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
        command.Parameters.AddWithValue("@role", user.Role ?? "User");
        command.Parameters.AddWithValue("@status", user.Status);
        command.Parameters.AddWithValue("@lang", user.Language ?? "English (US)");
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
                        Role = @role,
                        Status = @status,
                        Language = @lang,
                        AvatarBase64 = @avatar
                    WHERE Id = @id;
                    """;
                updateCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
                updateCmd.Parameters.AddWithValue("@name", odooUser.Name);
                updateCmd.Parameters.AddWithValue("@login", odooUser.Login);
                updateCmd.Parameters.AddWithValue("@email", odooUser.Email);
                updateCmd.Parameters.AddWithValue("@phone", odooUser.Phone);
                updateCmd.Parameters.AddWithValue("@role", odooUser.Role);
                updateCmd.Parameters.AddWithValue("@status", odooUser.Status);
                updateCmd.Parameters.AddWithValue("@lang", odooUser.Language);
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
                    INSERT INTO Users (OdooId, Name, Login, Email, Phone, Password, Pin, Role, Status, Language, AvatarBase64, IsVisibleInPos, IsPurpleTheme)
                    VALUES (@odooId, @name, @login, @email, @phone, @pass, @pin, @role, @status, @lang, @avatar, 1, @theme);
                    """;
                insertCmd.Parameters.AddWithValue("@odooId", odooUser.OdooId);
                insertCmd.Parameters.AddWithValue("@name", odooUser.Name);
                insertCmd.Parameters.AddWithValue("@login", odooUser.Login);
                insertCmd.Parameters.AddWithValue("@email", odooUser.Email);
                insertCmd.Parameters.AddWithValue("@phone", odooUser.Phone);
                insertCmd.Parameters.AddWithValue("@pass", "••••••••");
                insertCmd.Parameters.AddWithValue("@pin", "");
                insertCmd.Parameters.AddWithValue("@role", odooUser.Role);
                insertCmd.Parameters.AddWithValue("@status", odooUser.Status);
                insertCmd.Parameters.AddWithValue("@lang", odooUser.Language);
                insertCmd.Parameters.AddWithValue("@avatar", (object?)odooUser.AvatarBase64 ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@theme", odooUser.OdooId % 2 == 1 ? 1 : 0);

                await insertCmd.ExecuteNonQueryAsync();
                imported++;
            }
        }

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
    // 3. POS PAYMENT METHODS REPOSITORY (pos.payment.method)
    // =========================================================================
    public async Task<List<PosPaymentMethodRecord>> LoadAllPaymentMethodsAsync()
    {
        var list = new List<PosPaymentMethodRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OdooId, Name, MethodType, JournalId, JournalName, CompanyId, CompanyName,
                   LinkedPosConfigs, Integration, ImageBase64
            FROM PosPaymentMethods
            ORDER BY COALESCE(OdooId, Id) ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new PosPaymentMethodRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                Name = reader.GetString(2),
                MethodType = reader.IsDBNull(3) ? "cash" : reader.GetString(3),
                JournalId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                JournalName = reader.IsDBNull(5) ? "" : reader.GetString(5),
                CompanyId = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                CompanyName = reader.IsDBNull(7) ? "" : reader.GetString(7),
                LinkedPosConfigs = reader.IsDBNull(8) ? "" : reader.GetString(8),
                Integration = reader.IsDBNull(9) ? "None required" : reader.GetString(9),
                ImageBase64 = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }

        return list;
    }

    public async Task<(int imported, int updated, int deleted)> UpsertOdooPaymentMethodsAsync(List<PosPaymentMethodDto> methods)
    {
        int imported = 0, updated = 0, deleted = 0;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        var odooIds = methods.Select(m => m.OdooId).ToHashSet();

        foreach (var m in methods)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosPaymentMethods WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", m.OdooId);
            var existingObj = await checkCmd.ExecuteScalarAsync();

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingObj != null && existingObj != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE PosPaymentMethods
                    SET Name = @name, MethodType = @type, JournalId = @journalId, JournalName = @journalName,
                        CompanyId = @companyId, CompanyName = @companyName, LinkedPosConfigs = @shops,
                        Integration = @integration, ImageBase64 = @image, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO PosPaymentMethods (OdooId, Name, MethodType, JournalId, JournalName,
                                                   CompanyId, CompanyName, LinkedPosConfigs, Integration, ImageBase64)
                    VALUES (@odooId, @name, @type, @journalId, @journalName,
                            @companyId, @companyName, @shops, @integration, @image);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", m.OdooId);
            cmd.Parameters.AddWithValue("@name", m.Name);
            cmd.Parameters.AddWithValue("@type", m.MethodType ?? "cash");
            cmd.Parameters.AddWithValue("@journalId", (object?)m.JournalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@journalName", m.JournalName ?? "");
            cmd.Parameters.AddWithValue("@companyId", (object?)m.CompanyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@companyName", m.CompanyName ?? "");
            cmd.Parameters.AddWithValue("@shops", m.LinkedPosConfigs ?? "");
            cmd.Parameters.AddWithValue("@integration", m.Integration ?? "None required");
            cmd.Parameters.AddWithValue("@image", (object?)m.ImageBase64 ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId FROM PosPaymentMethods;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                if (!oId.HasValue || !odooIds.Contains(oId.Value)) delIds.Add(id);
            }
        }

        foreach (long delId in delIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = "DELETE FROM PosPaymentMethods WHERE Id = @id;";
            delCmd.Parameters.AddWithValue("@id", delId);
            await delCmd.ExecuteNonQueryAsync();
            deleted++;
        }

        await transaction.CommitAsync();
        return (imported, updated, deleted);
    }

    public async Task UpdatePaymentMethodSyncStatusAsync(DateTime syncedAt, string status)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE OdooConfig SET LastPaymentMethodsSyncedAt = @syncedAt WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@syncedAt", syncedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetLastPaymentMethodSyncStatusAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT LastPaymentMethodsSyncedAt FROM OdooConfig WHERE Id = 1;";
        var res = await cmd.ExecuteScalarAsync();
        return res is DBNull or null ? null : res.ToString();
    }

    // =========================================================================
    // 4. POS CATEGORIES REPOSITORY (pos.category)
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
            int colorIndex = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
            string? imgBase64 = reader.IsDBNull(9) ? null : reader.GetString(9);

            list.Add(new PosCategoryRecord
            {
                Id = reader.GetInt64(0),
                OdooId = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                Name = reader.GetString(2),
                ParentCategoryId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                ParentCategoryName = reader.IsDBNull(4) ? null : reader.GetString(4),
                LinkedPosConfigs = reader.IsDBNull(5) ? null : reader.GetString(5),
                ColorIndex = colorIndex,
                ColorHex = reader.IsDBNull(7) ? PosCategoryRecord.GetOdooColorHex(colorIndex) : reader.GetString(7),
                Sequence = reader.IsDBNull(8) ? 10 : reader.GetInt32(8),
                ImageBase64 = imgBase64
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
                SET Name = @name, ParentCategoryId = @parentId, ParentCategoryName = @parentName,
                    LinkedPosConfigs = @shops, ColorIndex = @colorIndex, ColorHex = @colorHex,
                    Sequence = @sequence, ImageBase64 = @image, UpdatedAt = CURRENT_TIMESTAMP
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
                category.Id = generatedId;
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

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingObj != null && existingObj != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE PosCategories
                    SET Name = @name, ParentCategoryId = @parentId, ParentCategoryName = @parentName,
                        LinkedPosConfigs = @shops, ColorIndex = @colorIndex, ColorHex = @colorHex,
                        Sequence = @sequence, ImageBase64 = @image, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO PosCategories (OdooId, Name, ParentCategoryId, ParentCategoryName, LinkedPosConfigs, ColorIndex, ColorHex, Sequence, ImageBase64)
                    VALUES (@odooId, @name, @parentId, @parentName, @shops, @colorIndex, @colorHex, @sequence, @image);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", cat.OdooId);
            cmd.Parameters.AddWithValue("@name", cat.Name);
            cmd.Parameters.AddWithValue("@parentId", (object?)cat.ParentCategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@parentName", (object?)cat.ParentCategoryName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@shops", (object?)cat.LinkedPosConfigs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@colorIndex", cat.ColorIndex);
            cmd.Parameters.AddWithValue("@colorHex", cat.ColorHex);
            cmd.Parameters.AddWithValue("@sequence", cat.Sequence);
            cmd.Parameters.AddWithValue("@image", (object?)cat.ImageBase64 ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

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
                    toDelete.Add(id);
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
    // 5. PRODUCT ATTRIBUTES & VALUES REPOSITORY
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

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId != null && existingId != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE ProductAttributes
                    SET Name = @name, DisplayType = @displayType, CreateVariant = @createVariant,
                        NumberRelatedProducts = @relatedProducts, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                attrsUpdated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO ProductAttributes (OdooId, Name, DisplayType, CreateVariant, NumberRelatedProducts)
                    VALUES (@odooId, @name, @displayType, @createVariant, @relatedProducts);
                    """;
                attrsImported++;
            }

            cmd.Parameters.AddWithValue("@odooId", attr.OdooId);
            cmd.Parameters.AddWithValue("@name", attr.Name);
            cmd.Parameters.AddWithValue("@displayType", attr.DisplayType);
            cmd.Parameters.AddWithValue("@createVariant", attr.CreateVariant);
            cmd.Parameters.AddWithValue("@relatedProducts", attr.NumberRelatedProducts);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var val in values)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM ProductAttributeValues WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", val.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId != null && existingId != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE ProductAttributeValues
                    SET AttributeOdooId = @attrId, Name = @name, IsCustom = @custom,
                        HtmlColor = @color, ImageBase64 = @image, DefaultExtraPrice = @extra,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                valsUpdated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO ProductAttributeValues (OdooId, AttributeOdooId, Name, IsCustom, HtmlColor, ImageBase64, DefaultExtraPrice)
                    VALUES (@odooId, @attrId, @name, @custom, @color, @image, @extra);
                    """;
                valsImported++;
            }

            cmd.Parameters.AddWithValue("@odooId", val.OdooId);
            cmd.Parameters.AddWithValue("@attrId", val.AttributeOdooId);
            cmd.Parameters.AddWithValue("@name", val.Name);
            cmd.Parameters.AddWithValue("@custom", val.IsCustom ? 1 : 0);
            cmd.Parameters.AddWithValue("@color", (object?)val.HtmlColor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@image", (object?)val.ImageBase64 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@extra", val.DefaultExtraPrice);
            await cmd.ExecuteNonQueryAsync();
        }

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
    // 6. PRODUCT TAGS REPOSITORY
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

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId != null && existingId != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE ProductTags
                    SET Name = @name, ColorIndex = @color, ProductsCount = @count, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO ProductTags (OdooId, Name, ColorIndex, ProductsCount)
                    VALUES (@odooId, @name, @color, @count);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", tag.OdooId);
            cmd.Parameters.AddWithValue("@name", tag.Name);
            cmd.Parameters.AddWithValue("@color", tag.ColorIndex);
            cmd.Parameters.AddWithValue("@count", tag.ProductsCount);
            await cmd.ExecuteNonQueryAsync();
        }

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
    // 7. PRODUCTS REPOSITORY (product.template)
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
                   COALESCE(c.ColorHex, '#F7CD1F'),
                   COALESCE(p.InternalCategoryName, 'All'),
                   COALESCE(p.QtyAvailable, 0.0),
                   COALESCE(p.SaleOk, 1),
                   COALESCE(p.PurchaseOk, 1),
                   COALESCE(p.AvailableInPos, 1),
                   COALESCE(p.TrackInventory, 0),
                   COALESCE(p.ToWeight, 0),
                   COALESCE(p.SalesTaxLabel, '15%'),
                   COALESCE(p.SalesTaxRate, 15.0),
                   COALESCE(p.PurchaseTaxLabel, '15%'),
                   COALESCE(p.UomName, 'Units'),
                   COALESCE(p.CompanyName, ''),
                   COALESCE(p.PosDescription, '')
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
                CategoryColorHex = reader.IsDBNull(15) ? "#F7CD1F" : reader.GetString(15),
                InternalCategoryName = reader.IsDBNull(16) ? "All" : reader.GetString(16),
                QtyAvailable = reader.IsDBNull(17) ? 0.00m : Convert.ToDecimal(reader.GetDouble(17)),
                SaleOk = reader.IsDBNull(18) || reader.GetInt32(18) != 0,
                PurchaseOk = reader.IsDBNull(19) || reader.GetInt32(19) != 0,
                AvailableInPos = reader.IsDBNull(20) || reader.GetInt32(20) != 0,
                TrackInventory = !reader.IsDBNull(21) && reader.GetInt32(21) != 0,
                ToWeight = !reader.IsDBNull(22) && reader.GetInt32(22) != 0,
                SalesTaxLabel = reader.IsDBNull(23) ? "15%" : reader.GetString(23),
                SalesTaxRate = reader.IsDBNull(24) ? 15.0m : Convert.ToDecimal(reader.GetDouble(24)),
                PurchaseTaxLabel = reader.IsDBNull(25) ? "15%" : reader.GetString(25),
                UomName = reader.IsDBNull(26) ? "Units" : reader.GetString(26),
                CompanyName = reader.IsDBNull(27) ? "" : reader.GetString(27),
                PosDescription = reader.IsDBNull(28) ? "" : reader.GetString(28)
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

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId != null && existingId != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE Products
                    SET Name = @name, ProductType = @type, ListPrice = @listPrice, StandardPrice = @stdPrice,
                        DefaultCode = @sku, Barcode = @barcode, PosCategoryId = @catId, PosCategoryName = @catName,
                        InternalCategoryName = @internalCat, VariantCount = @variants, QtyAvailable = @qty,
                        SaleOk = @saleOk, PurchaseOk = @purchaseOk, AvailableInPos = @availPos,
                        TrackInventory = @trackInv, ToWeight = @toWeight,
                        SalesTaxLabel = @sTaxLbl, SalesTaxRate = @sTaxRate, PurchaseTaxLabel = @pTaxLbl,
                        UomName = @uom, CompanyName = @company, PosDescription = @posDesc,
                        ImageBase64 = @image, TagsJson = @tags, AttributesJson = @attrs, ComboChoicesJson = @combos,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO Products (OdooId, Name, ProductType, ListPrice, StandardPrice, DefaultCode, Barcode,
                                         PosCategoryId, PosCategoryName, InternalCategoryName, VariantCount, QtyAvailable,
                                         SaleOk, PurchaseOk, AvailableInPos, TrackInventory, ToWeight,
                                         SalesTaxLabel, SalesTaxRate, PurchaseTaxLabel, UomName, CompanyName, PosDescription,
                                         ImageBase64, TagsJson, AttributesJson, ComboChoicesJson)
                    VALUES (@odooId, @name, @type, @listPrice, @stdPrice, @sku, @barcode,
                            @catId, @catName, @internalCat, @variants, @qty,
                            @saleOk, @purchaseOk, @availPos, @trackInv, @toWeight,
                            @sTaxLbl, @sTaxRate, @pTaxLbl, @uom, @company, @posDesc,
                            @image, @tags, @attrs, @combos);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", p.OdooId);
            cmd.Parameters.AddWithValue("@name", p.Name);
            cmd.Parameters.AddWithValue("@type", p.ProductType);
            cmd.Parameters.AddWithValue("@listPrice", (double)p.ListPrice);
            cmd.Parameters.AddWithValue("@stdPrice", (double)p.StandardPrice);
            cmd.Parameters.AddWithValue("@sku", p.DefaultCode ?? "");
            cmd.Parameters.AddWithValue("@barcode", p.Barcode ?? "");
            cmd.Parameters.AddWithValue("@catId", (object?)p.PosCategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@catName", (object?)p.PosCategoryName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@internalCat", p.InternalCategoryName ?? "All");
            cmd.Parameters.AddWithValue("@variants", p.VariantCount);
            cmd.Parameters.AddWithValue("@qty", (double)p.QtyAvailable);
            cmd.Parameters.AddWithValue("@saleOk", p.SaleOk ? 1 : 0);
            cmd.Parameters.AddWithValue("@purchaseOk", p.PurchaseOk ? 1 : 0);
            cmd.Parameters.AddWithValue("@availPos", p.AvailableInPos ? 1 : 0);
            cmd.Parameters.AddWithValue("@trackInv", p.TrackInventory ? 1 : 0);
            cmd.Parameters.AddWithValue("@toWeight", p.ToWeight ? 1 : 0);
            cmd.Parameters.AddWithValue("@sTaxLbl", p.SalesTaxLabel ?? "15%");
            cmd.Parameters.AddWithValue("@sTaxRate", (double)p.SalesTaxRate);
            cmd.Parameters.AddWithValue("@pTaxLbl", p.PurchaseTaxLabel ?? "15%");
            cmd.Parameters.AddWithValue("@uom", p.UomName ?? "Units");
            cmd.Parameters.AddWithValue("@company", p.CompanyName ?? "");
            cmd.Parameters.AddWithValue("@posDesc", p.PosDescription ?? "");
            cmd.Parameters.AddWithValue("@image", (object?)p.ImageBase64 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", (object?)p.TagsJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@attrs", (object?)p.AttributesJson ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@combos", (object?)p.ComboChoicesJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

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
    // 8. COMBO CHOICES REPOSITORY
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

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId != null && existingId != DBNull.Value)
            {
                cmd.CommandText = "UPDATE ComboChoices SET Name = @name, UpdatedAt = CURRENT_TIMESTAMP WHERE OdooId = @odooId;";
                updated++;
            }
            else
            {
                cmd.CommandText = "INSERT INTO ComboChoices (OdooId, Name) VALUES (@odooId, @name);";
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", c.OdooId);
            cmd.Parameters.AddWithValue("@name", c.Name);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var l in lines)
        {
            await using var checkLine = connection.CreateCommand();
            checkLine.Transaction = transaction;
            checkLine.CommandText = "SELECT Id FROM ComboChoiceLines WHERE OdooId = @odooId LIMIT 1;";
            checkLine.Parameters.AddWithValue("@odooId", l.OdooId);
            var existingLineId = await checkLine.ExecuteScalarAsync();

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingLineId != null && existingLineId != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE ComboChoiceLines
                    SET ComboId = @comboId, ProductId = @productId, ProductName = @productName,
                        OriginalPrice = @origPrice, ExtraPrice = @extraPrice, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO ComboChoiceLines (OdooId, ComboId, ProductId, ProductName, OriginalPrice, ExtraPrice)
                    VALUES (@odooId, @comboId, @productId, @productName, @origPrice, @extraPrice);
                    """;
            }

            cmd.Parameters.AddWithValue("@odooId", l.OdooId);
            cmd.Parameters.AddWithValue("@comboId", l.ComboId);
            cmd.Parameters.AddWithValue("@productId", (object?)l.ProductId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@productName", l.ProductName);
            cmd.Parameters.AddWithValue("@origPrice", (double)l.OriginalPrice);
            cmd.Parameters.AddWithValue("@extraPrice", (double)l.ExtraPrice);
            await cmd.ExecuteNonQueryAsync();
        }

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
    // 9. POS SESSIONS REPOSITORY (pos.session)
    // =========================================================================
    public async Task<List<PosSessionRecord>> LoadAllSessionsAsync()
    {
        var result = new List<PosSessionRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Ensure any active session on PosConfigs has a corresponding row in PosSessions
        try
        {
            await using var syncCfgSessCmd = connection.CreateCommand();
            syncCfgSessCmd.CommandText = """
                INSERT INTO PosSessions (OdooId, Name, PosConfigId, PosConfigName, UserId, UserName, StartAt, State, OpeningBalance, IsSynced)
                SELECT c.CurrentSessionId,
                       COALESCE(NULLIF(c.CurrentSessionName, ''), c.Name || '/00001'),
                       c.OdooId,
                       c.Name,
                       c.ActiveUserId,
                       COALESCE(NULLIF(c.ActiveUserName, ''), 'Mitchell Admin'),
                       COALESCE(c.UpdatedAt, CURRENT_TIMESTAMP),
                       COALESCE(NULLIF(c.CurrentSessionState, ''), 'opened'),
                       COALESCE(c.OpeningBalance, 0.0),
                       0
                FROM PosConfigs c
                WHERE LOWER(COALESCE(c.CurrentSessionState, 'closed')) != 'closed'
                  AND NOT EXISTS (
                      SELECT 1 FROM PosSessions s
                      WHERE (c.CurrentSessionId IS NOT NULL AND (s.OdooId = c.CurrentSessionId OR s.Id = c.CurrentSessionId))
                         OR (s.PosConfigId = c.OdooId AND LOWER(COALESCE(s.State, 'opened')) != 'closed')
                         OR (c.CurrentSessionName IS NOT NULL AND c.CurrentSessionName != '' AND s.Name = c.CurrentSessionName)
                  );
                """;
            await syncCfgSessCmd.ExecuteNonQueryAsync();
        }
        catch { }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.Id, s.OdooId, s.Name, s.PosConfigId, s.PosConfigName, s.UserId, s.UserName,
                   COALESCE(s.UserAvatarBase64, u.AvatarBase64),
                   s.StartAt, s.StopAt, s.State, s.OpeningBalance, s.ClosingBalance,
                   s.OrdersCount, s.TotalPaymentsAmount, s.Employees
            FROM PosSessions s
            LEFT JOIN Users u ON s.UserId = u.OdooId
            ORDER BY COALESCE(s.OdooId, s.Id) DESC;
            """;

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
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
        }

        // Compute live paid orders count and total payments from PosOrders for each session
        foreach (var sess in result)
        {
            try
            {
                await using var statCmd = connection.CreateCommand();
                statCmd.CommandText = """
                    SELECT COUNT(*), COALESCE(SUM(AmountTotal), 0.0)
                    FROM PosOrders
                    WHERE LOWER(COALESCE(State, 'paid')) NOT IN ('draft', 'ongoing', 'new', 'cancel', 'cancelled')
                      AND (
                          (@odooId IS NOT NULL AND (SessionOdooId = @odooId OR SessionId = @odooId))
                          OR SessionId = @localId
                          OR (@sessName != '' AND LOWER(SessionName) = LOWER(@sessName))
                      );
                    """;
                statCmd.Parameters.AddWithValue("@odooId", (object?)sess.OdooId ?? DBNull.Value);
                statCmd.Parameters.AddWithValue("@localId", sess.Id);
                statCmd.Parameters.AddWithValue("@sessName", sess.Name ?? "");

                await using var sr = await statCmd.ExecuteReaderAsync();
                if (await sr.ReadAsync())
                {
                    int liveCount = sr.IsDBNull(0) ? 0 : sr.GetInt32(0);
                    decimal liveTotal = sr.IsDBNull(1) ? 0.00m : Convert.ToDecimal(sr.GetDouble(1));
                    if (liveCount > sess.OrdersCount)
                        sess.OrdersCount = liveCount;
                    if (liveTotal > sess.TotalPaymentsAmount)
                        sess.TotalPaymentsAmount = liveTotal;
                }
            }
            catch { }
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
            checkCmd.CommandText = "SELECT Id, COALESCE(IsSynced, 1) FROM PosSessions WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", s.OdooId);

            long? existingId = null;
            bool isLocalUnsynced = false;
            await using (var r = await checkCmd.ExecuteReaderAsync())
            {
                if (await r.ReadAsync())
                {
                    existingId = r.GetInt64(0);
                    isLocalUnsynced = r.GetInt32(1) == 0;
                }
            }

            if (isLocalUnsynced)
            {
                continue;
            }

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId.HasValue)
            {
                cmd.CommandText = """
                    UPDATE PosSessions
                    SET Name = @name, PosConfigId = @posConfigId, PosConfigName = @posConfigName,
                        UserId = @userId, UserName = @userName, UserAvatarBase64 = @avatar,
                        StartAt = @startAt, StopAt = @stopAt, State = @state,
                        OpeningBalance = @openBal, ClosingBalance = @closeBal,
                        OrdersCount = @ordersCount, TotalPaymentsAmount = @totalPayments,
                        Employees = @employees, IsSynced = 1, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO PosSessions (OdooId, Name, PosConfigId, PosConfigName, UserId, UserName, UserAvatarBase64,
                                            StartAt, StopAt, State, OpeningBalance, ClosingBalance, OrdersCount, TotalPaymentsAmount, Employees, IsSynced)
                    VALUES (@odooId, @name, @posConfigId, @posConfigName, @userId, @userName, @avatar,
                            @startAt, @stopAt, @state, @openBal, @closeBal, @ordersCount, @totalPayments, @employees, 1);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", s.OdooId);
            cmd.Parameters.AddWithValue("@name", s.Name);
            cmd.Parameters.AddWithValue("@posConfigId", (object?)s.PosConfigId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@posConfigName", s.PosConfigName ?? "");
            cmd.Parameters.AddWithValue("@userId", (object?)s.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@userName", s.UserName ?? "");
            cmd.Parameters.AddWithValue("@avatar", (object?)s.UserAvatarBase64 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@startAt", s.StartAt.HasValue ? s.StartAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@stopAt", s.StopAt.HasValue ? s.StopAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@state", s.State);
            cmd.Parameters.AddWithValue("@openBal", (double)s.OpeningBalance);
            cmd.Parameters.AddWithValue("@closeBal", (double)s.ClosingBalance);
            cmd.Parameters.AddWithValue("@ordersCount", s.OrdersCount);
            cmd.Parameters.AddWithValue("@totalPayments", (double)s.TotalPaymentsAmount);
            cmd.Parameters.AddWithValue("@employees", s.Employees ?? "");
            await cmd.ExecuteNonQueryAsync();
        }

        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId, COALESCE(IsSynced, 1) FROM PosSessions;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                bool isSynced = reader.GetInt32(2) != 0;
                // Keep local sessions (where OdooId is null); only delete synced Odoo sessions that no longer exist on the server
                if (isSynced && oId.HasValue && !odooIds.Contains(oId.Value))
                    delIds.Add(id);
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
    // 10. POS ORDERS & ORDER LINES REPOSITORY (pos.order & pos.order.line)
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
                SELECT o.Id, o.OdooId, o.Name, o.PosReference, o.TrackingNumber, o.SessionId, o.SessionOdooId, o.SessionName, o.PosConfigName,
                       o.DateOrder, o.CashierId, o.CashierName, COALESCE(o.CashierAvatarBase64, u.AvatarBase64), o.FiscalPosition,
                       o.AmountTotal, o.AmountTax, o.AmountPaid, o.Margin, o.MarginRatio, o.State, o.IsEdited, o.Company, o.Note, o.LinesJson,
                       COALESCE(o.Uuid, ''), COALESCE(o.IsSynced, 1), COALESCE(o.CustomerName, '')
                FROM PosOrders o
                LEFT JOIN Users u ON o.CashierId = u.OdooId
                WHERE o.SessionName = @filter OR o.PosConfigName = @filter OR CAST(o.SessionOdooId AS TEXT) = @filter
                ORDER BY COALESCE(o.OdooId, o.Id) DESC;
                """;
            cmd.Parameters.AddWithValue("@filter", sessionFilter.Trim());
        }
        else
        {
            cmd.CommandText = """
                SELECT o.Id, o.OdooId, o.Name, o.PosReference, o.TrackingNumber, o.SessionId, o.SessionOdooId, o.SessionName, o.PosConfigName,
                       o.DateOrder, o.CashierId, o.CashierName, COALESCE(o.CashierAvatarBase64, u.AvatarBase64), o.FiscalPosition,
                       o.AmountTotal, o.AmountTax, o.AmountPaid, o.Margin, o.MarginRatio, o.State, o.IsEdited, o.Company, o.Note, o.LinesJson,
                       COALESCE(o.Uuid, ''), COALESCE(o.IsSynced, 1), COALESCE(o.CustomerName, '')
                FROM PosOrders o
                LEFT JOIN Users u ON o.CashierId = u.OdooId
                ORDER BY COALESCE(o.OdooId, o.Id) DESC;
                """;
        }

        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                string uuid = reader.IsDBNull(24) || string.IsNullOrWhiteSpace(reader.GetString(24))
                    ? Guid.NewGuid().ToString()
                    : reader.GetString(24);

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
                    Note = reader.IsDBNull(22) ? "" : reader.GetString(22),
                    RefundedOrderIdsJson = reader.IsDBNull(23) ? null : reader.GetString(23),
                    Uuid = uuid,
                    IsSynced = reader.IsDBNull(25) || reader.GetInt32(25) != 0,
                    CustomerName = reader.IsDBNull(26) ? "" : reader.GetString(26)
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
                    Amount = payReader.IsDBNull(11) ? 0.00m : Convert.ToDecimal(lineOrPayAmount(payReader, 11)),
                    CurrencyName = payReader.IsDBNull(12) ? "MAD" : payReader.GetString(12),
                    ForeignCurrency = payReader.IsDBNull(13) ? "—" : payReader.GetString(13),
                    PaymentDate = payReader.IsDBNull(14) ? null : (DateTime.TryParse(payReader.GetString(14), out var pd) ? pd : null)
                });
            }
        }

        static double lineOrPayAmount(SqliteDataReader r, int idx) => r.GetDouble(idx);
        // Link Original Orders <-> Refund Orders bidirectionally
        var byOdooId = result.Where(o => o.OdooId.HasValue).ToDictionary(o => o.OdooId!.Value, o => o);
        var byName = result
            .Where(o => !string.IsNullOrWhiteSpace(o.Name) && o.Name != "/")
            .GroupBy(o => o.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        static void AddLink(PosOrderRecord a, PosOrderRecord b)
        {
            if (a == b) return;
            if (!a.LinkedRefundOrders.Contains(b)) a.LinkedRefundOrders.Add(b);
            if (!b.LinkedRefundOrders.Contains(a)) b.LinkedRefundOrders.Add(a);
        }

        foreach (var order in result)
        {
            if (!string.IsNullOrWhiteSpace(order.RefundedOrderIdsJson))
            {
                try
                {
                    var ids = JsonSerializer.Deserialize<List<int>>(order.RefundedOrderIdsJson);
                    if (ids != null)
                    {
                        foreach (int targetId in ids)
                        {
                            if (byOdooId.TryGetValue(targetId, out var origOrder))
                                AddLink(order, origOrder);
                        }
                    }
                }
                catch { }
            }

            if (order.Name.EndsWith(" REFUND", StringComparison.OrdinalIgnoreCase))
            {
                string baseName = order.Name[..^" REFUND".Length].Trim();
                if (byName.TryGetValue(baseName, out var origByName))
                    AddLink(order, origByName);
            }
        }

        foreach (var order in result)
        {
            order.NotifyLinkedRefundsChanged();
        }

        return result;
    }

    public async Task<(int NextSequence, int DeviceDigit)> GetNextOrderSequenceForShopAsync(
        string shopName,
        int configOdooId,
        int? sessionOdooId = null)
    {
        int maxSeq = 0;
        int detectedDeviceDigit = 2; // Matches Odoo 20's "262-1-000001" / "2001" device identifier when present
        bool deviceFound = false;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Name, PosReference, TrackingNumber
            FROM PosOrders
            WHERE PosConfigName = @shop
               OR (@sessId IS NOT NULL AND (SessionOdooId = @sessId OR SessionId = @sessId))
               OR PosReference LIKE @cfgPattern
            ORDER BY COALESCE(OdooId, Id) DESC;
            """;
        cmd.Parameters.AddWithValue("@shop", shopName ?? "");
        cmd.Parameters.AddWithValue("@sessId", (object?)sessionOdooId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cfgPattern", $"%-{configOdooId}-%");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string name = reader.IsDBNull(0) ? "" : reader.GetString(0);
            string posRef = reader.IsDBNull(1) ? "" : reader.GetString(1);
            string trackNum = reader.IsDBNull(2) ? "" : reader.GetString(2);

            // 1. Parse sequence from Name (e.g. "Furniture Shop - 000002" -> 2)
            int dashIdx = name.LastIndexOf('-');
            if (dashIdx >= 0 && dashIdx + 1 < name.Length)
            {
                string tail = name[(dashIdx + 1)..].Trim();
                if (int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out int nameSeq) && nameSeq > maxSeq)
                {
                    maxSeq = nameSeq;
                }
            }

            // 2. Parse sequence and device digit from PosReference (e.g. "262-1-000002")
            if (!string.IsNullOrWhiteSpace(posRef))
            {
                string[] parts = posRef.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 3)
                {
                    if (int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int refSeq) && refSeq > maxSeq)
                    {
                        maxSeq = refSeq;
                    }

                    // Only adopt device digit from canonical Odoo references where middle part == configOdooId
                    if (!deviceFound &&
                        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int refCfgId) &&
                        refCfgId == configOdooId &&
                        parts[0].Length >= 3 &&
                        char.IsDigit(parts[0][^1]))
                    {
                        int digit = parts[0][^1] - '0';
                        if (digit is >= 1 and <= 9)
                        {
                            detectedDeviceDigit = digit;
                            deviceFound = true;
                        }
                    }
                }
            }

            // 3. Fallback device digit detection from TrackingNumber (e.g. "2002" -> 2)
            if (!deviceFound && trackNum.Length == 4 && int.TryParse(trackNum, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tn))
            {
                int d = tn / 1000;
                if (d is >= 1 and <= 9)
                {
                    detectedDeviceDigit = d;
                    deviceFound = true;
                }
            }
        }

        return (maxSeq + 1, detectedDeviceDigit);
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
            checkCmd.CommandText = """
                SELECT Id FROM PosOrders
                WHERE OdooId = @odooId
                   OR (PosReference IS NOT NULL AND PosReference != '' AND PosReference = @refNum)
                LIMIT 1;
                """;
            checkCmd.Parameters.AddWithValue("@odooId", o.OdooId);
            checkCmd.Parameters.AddWithValue("@refNum", o.PosReference ?? "");
            var existingIdObj = await checkCmd.ExecuteScalarAsync();

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingIdObj != null && existingIdObj != DBNull.Value)
            {
                long existingId = Convert.ToInt64(existingIdObj);
                cmd.CommandText = """
                    UPDATE PosOrders
                    SET OdooId = @odooId, Name = @name, PosReference = @refNum, TrackingNumber = @trackNum,
                        SessionOdooId = @sessOdooId, SessionName = @sessName, PosConfigName = @cfgName,
                        DateOrder = @dateOrder, CashierId = @cashierId, CashierName = @cashierName,
                        CashierAvatarBase64 = @avatar, FiscalPosition = @fiscal,
                        AmountTotal = @total, AmountTax = @tax, AmountPaid = @paid,
                        Margin = @margin, MarginRatio = @marginRatio, State = @state,
                        IsEdited = @isEdited, Company = @company, Note = @note,
                        LinesJson = @linesJson, IsSynced = 1, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = @id;
                    """;
                cmd.Parameters.AddWithValue("@id", existingId);
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO PosOrders (OdooId, Uuid, Name, PosReference, TrackingNumber, SessionOdooId, SessionName, PosConfigName,
                                          DateOrder, CashierId, CashierName, CashierAvatarBase64, FiscalPosition, AmountTotal, AmountTax,
                                          AmountPaid, Margin, MarginRatio, State, IsEdited, Company, Note, LinesJson, IsSynced)
                    VALUES (@odooId, @uuid, @name, @refNum, @trackNum, @sessOdooId, @sessName, @cfgName,
                            @dateOrder, @cashierId, @cashierName, @avatar, @fiscal, @total, @tax,
                            @paid, @margin, @marginRatio, @state, @isEdited, @company, @note, @linesJson, 1);
                    """;
                cmd.Parameters.AddWithValue("@uuid", Guid.NewGuid().ToString());
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", o.OdooId);
            cmd.Parameters.AddWithValue("@name", o.Name);
            cmd.Parameters.AddWithValue("@refNum", o.PosReference);
            cmd.Parameters.AddWithValue("@trackNum", o.TrackingNumber);
            cmd.Parameters.AddWithValue("@sessOdooId", (object?)o.SessionOdooId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sessName", o.SessionName);
            cmd.Parameters.AddWithValue("@cfgName", o.PosConfigName);
            cmd.Parameters.AddWithValue("@dateOrder", o.DateOrder.HasValue ? o.DateOrder.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@cashierId", (object?)o.CashierId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@cashierName", o.CashierName);
            cmd.Parameters.AddWithValue("@avatar", (object?)o.CashierAvatarBase64 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fiscal", o.FiscalPosition);
            cmd.Parameters.AddWithValue("@total", (double)o.AmountTotal);
            cmd.Parameters.AddWithValue("@tax", (double)o.AmountTax);
            cmd.Parameters.AddWithValue("@paid", (double)o.AmountPaid);
            cmd.Parameters.AddWithValue("@margin", (double)o.Margin);
            cmd.Parameters.AddWithValue("@marginRatio", (double)o.MarginRatio);
            cmd.Parameters.AddWithValue("@state", o.State);
            cmd.Parameters.AddWithValue("@isEdited", o.IsEdited ? 1 : 0);
            cmd.Parameters.AddWithValue("@company", o.Company);
            cmd.Parameters.AddWithValue("@note", o.Note);
            cmd.Parameters.AddWithValue("@linesJson", (object?)o.LinesJson ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cleanSyncedLinesCmd = connection.CreateCommand())
        {
            cleanSyncedLinesCmd.Transaction = transaction;
            cleanSyncedLinesCmd.CommandText = """
                DELETE FROM PosOrderLines
                WHERE OdooId IS NULL
                  AND OrderId IN (SELECT Id FROM PosOrders WHERE OdooId IS NOT NULL AND COALESCE(IsSynced, 1) = 1);
                """;
            await cleanSyncedLinesCmd.ExecuteNonQueryAsync();
        }

        foreach (var l in lines)
        {
            await using var checkLine = connection.CreateCommand();
            checkLine.Transaction = transaction;
            checkLine.CommandText = "SELECT Id FROM PosOrderLines WHERE OdooId = @odooId LIMIT 1;";
            checkLine.Parameters.AddWithValue("@odooId", l.OdooId);
            var existingLine = await checkLine.ExecuteScalarAsync();

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingLine != null && existingLine != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE PosOrderLines
                    SET OrderOdooId = @orderOdooId, ProductId = @prodId, ProductName = @prodName, IsEdited = @isEdited,
                        Qty = @qty, PriceUnit = @unitPrice, Discount = @disc, Taxes = @taxes,
                        PriceSubtotal = @subtotal, PriceSubtotalIncl = @subtotalIncl, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO PosOrderLines (OdooId, OrderOdooId, ProductId, ProductName, IsEdited, Qty, PriceUnit, Discount, Taxes, PriceSubtotal, PriceSubtotalIncl)
                    VALUES (@odooId, @orderOdooId, @prodId, @prodName, @isEdited, @qty, @unitPrice, @disc, @taxes, @subtotal, @subtotalIncl);
                    """;
            }

            cmd.Parameters.AddWithValue("@odooId", l.OdooId);
            cmd.Parameters.AddWithValue("@orderOdooId", l.OrderOdooId);
            cmd.Parameters.AddWithValue("@prodId", (object?)l.ProductId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@prodName", l.ProductName);
            cmd.Parameters.AddWithValue("@isEdited", l.IsEdited ? 1 : 0);
            cmd.Parameters.AddWithValue("@qty", (double)l.Qty);
            cmd.Parameters.AddWithValue("@unitPrice", (double)l.PriceUnit);
            cmd.Parameters.AddWithValue("@disc", (double)l.Discount);
            cmd.Parameters.AddWithValue("@taxes", l.Taxes);
            cmd.Parameters.AddWithValue("@subtotal", (double)l.PriceSubtotal);
            cmd.Parameters.AddWithValue("@subtotalIncl", (double)l.PriceSubtotalIncl);
            await cmd.ExecuteNonQueryAsync();
        }

        var delLineIds = new List<long>();
        await using (var fetchLinesCmd = connection.CreateCommand())
        {
            fetchLinesCmd.Transaction = transaction;
            fetchLinesCmd.CommandText = "SELECT Id, OdooId FROM PosOrderLines WHERE OdooId IS NOT NULL;";
            await using var reader = await fetchLinesCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int oId = reader.GetInt32(1);
                if (!odooLineIds.Contains(oId)) delLineIds.Add(id);
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

        var delOrderIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId, COALESCE(IsSynced, 1), LOWER(COALESCE(State, 'paid')) FROM PosOrders;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                bool isSynced = reader.GetInt32(2) != 0;
                string state = reader.GetString(3);
                bool isDraft = state is "draft" or "ongoing" or "new";

                if (isSynced && !isDraft && (!oId.HasValue || !odooOrderIds.Contains(oId.Value)))
                    delOrderIds.Add(id);
            }
        }

        foreach (var delId in delOrderIds)
        {
            await using var delCmd = connection.CreateCommand();
            delCmd.Transaction = transaction;
            delCmd.CommandText = """
                DELETE FROM PosOrderLines WHERE OrderId = @id;
                DELETE FROM PosOrders WHERE Id = @id;
                """;
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
    // 11. POS PAYMENTS REPOSITORY (pos.payment)
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
                SELECT p.Id, p.OdooId, p.OrderId, p.OrderOdooId, p.OrderName,
                       p.SessionId,
                       COALESCE(s.OdooId, o.SessionOdooId, p.SessionOdooId),
                       COALESCE(s.Name, o.SessionName, p.SessionName),
                       p.CashierName, p.PaymentMethodId, p.PaymentMethodName,
                       p.Amount, p.CurrencyName, p.ForeignCurrency, p.PaymentDate
                FROM PosPayments p
                LEFT JOIN PosOrders o ON o.Id = p.OrderId
                LEFT JOIN PosSessions s ON s.Id = p.SessionId
                WHERE p.SessionName = @filter
                   OR o.SessionName = @filter
                   OR s.Name = @filter
                   OR CAST(p.SessionOdooId AS TEXT) = @filter
                   OR p.OrderName LIKE @likeFilter
                ORDER BY p.PaymentDate DESC, p.Id DESC;
                """;
            cmd.Parameters.AddWithValue("@filter", sessionFilter.Trim());
            cmd.Parameters.AddWithValue("@likeFilter", $"{sessionFilter.Trim()}%");
        }
        else
        {
            cmd.CommandText = """
                SELECT p.Id, p.OdooId, p.OrderId, p.OrderOdooId, p.OrderName,
                       p.SessionId,
                       COALESCE(s.OdooId, o.SessionOdooId, p.SessionOdooId),
                       COALESCE(s.Name, o.SessionName, p.SessionName),
                       p.CashierName, p.PaymentMethodId, p.PaymentMethodName,
                       p.Amount, p.CurrencyName, p.ForeignCurrency, p.PaymentDate
                FROM PosPayments p
                LEFT JOIN PosOrders o ON o.Id = p.OrderId
                LEFT JOIN PosSessions s ON s.Id = p.SessionId
                ORDER BY p.PaymentDate DESC, p.Id DESC;
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

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId != null && existingId != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE PosPayments
                    SET OrderOdooId = @orderOdooId, OrderName = @orderName, SessionOdooId = @sessOdooId, SessionName = @sessName,
                        CashierName = @cashier, PaymentMethodId = @methodId, PaymentMethodName = @methodName, Amount = @amount,
                        CurrencyName = @curr, ForeignCurrency = @foreignCurr, PaymentDate = @date, IsSynced = 1, UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO PosPayments (OdooId, OrderOdooId, OrderName, SessionOdooId, SessionName, CashierName,
                                            PaymentMethodId, PaymentMethodName, Amount, CurrencyName, ForeignCurrency, PaymentDate, IsSynced)
                    VALUES (@odooId, @orderOdooId, @orderName, @sessOdooId, @sessName, @cashier,
                            @methodId, @methodName, @amount, @curr, @foreignCurr, @date, 1);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", p.OdooId);
            cmd.Parameters.AddWithValue("@orderOdooId", (object?)p.OrderOdooId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@orderName", p.OrderName);
            cmd.Parameters.AddWithValue("@sessOdooId", (object?)p.SessionOdooId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sessName", p.SessionName);
            cmd.Parameters.AddWithValue("@cashier", p.CashierName);
            cmd.Parameters.AddWithValue("@methodId", (object?)p.PaymentMethodId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@methodName", p.PaymentMethodName);
            cmd.Parameters.AddWithValue("@amount", (double)p.Amount);
            cmd.Parameters.AddWithValue("@curr", p.CurrencyName);
            cmd.Parameters.AddWithValue("@foreignCurr", p.ForeignCurrency);
            cmd.Parameters.AddWithValue("@date", p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        var delIds = new List<long>();
        await using (var fetchCmd = connection.CreateCommand())
        {
            fetchCmd.Transaction = transaction;
            fetchCmd.CommandText = "SELECT Id, OdooId, COALESCE(IsSynced, 1) FROM PosPayments;";
            await using var reader = await fetchCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = reader.GetInt64(0);
                int? oId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                bool isSynced = reader.GetInt32(2) != 0;
                if (isSynced && (!oId.HasValue || !odooIds.Contains(oId.Value)))
                    delIds.Add(id);
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
    // 12. POS CONFIGURATIONS (pos.config) REPOSITORY
    // =========================================================================
    public async Task<List<PosConfigRecord>> LoadAllPosConfigsAsync()
    {
        var result = new List<PosConfigRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT c.Id, c.OdooId, c.Name, c.CurrentSessionId, c.CurrentSessionName, c.CurrentSessionState,
                   c.LastClosingDate, c.LastClosingBalance, c.OpeningBalance,
                   c.ActiveUserId, c.ActiveUserName, COALESCE(c.ActiveUserAvatar, u.AvatarBase64), c.RecentSalesPointsJson,
                   COALESCE(c.SelectedPaymentMethodsCsv, 'Cash,Card,Customer Account'),
                   COALESCE(c.IsOneClickPaymentEnabled, 0),
                   COALESCE(c.OneClickPaymentMethodsCsv, 'Cash'),
                   COALESCE(c.IsRestrictCategoriesEnabled, 0),
                   COALESCE(c.RestrictedCategoriesCsv, ''),
                   COALESCE(c.IsVariantsEnabled, 1),
                   COALESCE(c.IsAutomaticReceiptPrintingEnabled, 0),
                   COALESCE(c.IsPreventLineMergingEnabled, 0),
                   COALESCE(c.IsPrintLastReceiptEnabled, 0),
                   COALESCE(c.IsCustomOrderReceiptsEnabled, 0),
                   c.CustomOrderReceiptDesignId,
                   COALESCE(c.IsHideOpeningCashControlEnabled, 0),
                   COALESCE(c.DefaultOpeningCashAmount, 0.0),
                   COALESCE(c.IsSecondReceiptEnabled, 0),
                   c.SecondReceiptDesignId,
                   COALESCE(c.IsHideClosingRegisterEnabled, 0),
                   c.CustomSalesReceiptDesignId,
                   COALESCE(c.IsHideReviewOrdersEnabled, 0),
                   COALESCE(c.NumbersFirstOrProducts, 'products')
            FROM PosConfigs c
            LEFT JOIN Users u ON c.ActiveUserId = u.OdooId
            ORDER BY COALESCE(c.OdooId, c.Id) ASC;
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
                RecentSalesPointsJson = reader.IsDBNull(12) ? "[]" : reader.GetString(12),
                SelectedPaymentMethodsCsv = reader.IsDBNull(13) ? "Cash,Card,Customer Account" : reader.GetString(13),
                IsOneClickPaymentEnabled = !reader.IsDBNull(14) && reader.GetInt32(14) != 0,
                OneClickPaymentMethodsCsv = reader.IsDBNull(15) ? "Cash" : reader.GetString(15),
                IsRestrictCategoriesEnabled = !reader.IsDBNull(16) && reader.GetInt32(16) != 0,
                RestrictedCategoriesCsv = reader.IsDBNull(17) ? "" : reader.GetString(17),
                IsVariantsEnabled = reader.IsDBNull(18) || reader.GetInt32(18) != 0,
                IsAutomaticReceiptPrintingEnabled = !reader.IsDBNull(19) && reader.GetInt32(19) != 0,
                IsPreventLineMergingEnabled = !reader.IsDBNull(20) && reader.GetInt32(20) != 0,
                IsPrintLastReceiptEnabled = !reader.IsDBNull(21) && reader.GetInt32(21) != 0,
                IsCustomOrderReceiptsEnabled = !reader.IsDBNull(22) && reader.GetInt32(22) != 0,
                CustomOrderReceiptDesignId = reader.IsDBNull(23) ? null : reader.GetInt64(23),
                IsHideOpeningCashControlEnabled = !reader.IsDBNull(24) && reader.GetInt32(24) != 0,
                DefaultOpeningCashAmount = reader.IsDBNull(25) ? 0.00m : Convert.ToDecimal(reader.GetDouble(25)),
                IsSecondReceiptEnabled = !reader.IsDBNull(26) && reader.GetInt32(26) != 0,
                SecondReceiptDesignId = reader.IsDBNull(27) ? null : reader.GetInt64(27),
                IsHideClosingRegisterEnabled = !reader.IsDBNull(28) && reader.GetInt32(28) != 0,
                CustomSalesReceiptDesignId = reader.IsDBNull(29) ? null : reader.GetInt64(29),
                IsHideReviewOrdersEnabled = !reader.IsDBNull(30) && reader.GetInt32(30) != 0,
                NumbersFirstOrProducts = reader.IsDBNull(31) ? "products" : reader.GetString(31)
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

        var pendingConfigIds = new HashSet<int>();
        await using (var pendCmd = connection.CreateCommand())
        {
            pendCmd.Transaction = transaction;
            pendCmd.CommandText = """
                SELECT DISTINCT PosConfigOdooId
                FROM PendingSyncQueue
                WHERE Status != 'Completed' AND PosConfigOdooId IS NOT NULL;
                """;
            await using var pr = await pendCmd.ExecuteReaderAsync();
            while (await pr.ReadAsync())
            {
                if (!pr.IsDBNull(0)) pendingConfigIds.Add(pr.GetInt32(0));
            }
        }

        foreach (var c in configs)
        {
            await using var checkCmd = connection.CreateCommand();
            checkCmd.Transaction = transaction;
            checkCmd.CommandText = "SELECT Id FROM PosConfigs WHERE OdooId = @odooId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@odooId", c.OdooId);
            var existingId = await checkCmd.ExecuteScalarAsync();

            if (existingId != null && existingId != DBNull.Value && pendingConfigIds.Contains(c.OdooId))
            {
                await using var sparkCmd = connection.CreateCommand();
                sparkCmd.Transaction = transaction;
                sparkCmd.CommandText = """
                    UPDATE PosConfigs
                    SET Name = @name, RecentSalesPointsJson = @salesPoints,
                        SelectedPaymentMethodsCsv = @payMethods,
                        IsOneClickPaymentEnabled = @oneClickEnabled,
                        OneClickPaymentMethodsCsv = @oneClickMethods,
                        IsRestrictCategoriesEnabled = @restrictCatEnabled,
                        RestrictedCategoriesCsv = @restrictedCats,
                        IsVariantsEnabled = @variantsEnabled,
                        IsAutomaticReceiptPrintingEnabled = @autoPrint,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                sparkCmd.Parameters.AddWithValue("@name", c.Name);
                sparkCmd.Parameters.AddWithValue("@salesPoints", c.RecentSalesPointsJson ?? "[]");
                sparkCmd.Parameters.AddWithValue("@payMethods", c.SelectedPaymentMethodsCsv ?? "Cash,Card,Customer Account");
                sparkCmd.Parameters.AddWithValue("@oneClickEnabled", c.IsOneClickPaymentEnabled ? 1 : 0);
                sparkCmd.Parameters.AddWithValue("@oneClickMethods", c.OneClickPaymentMethod ?? "Cash");
                sparkCmd.Parameters.AddWithValue("@restrictCatEnabled", c.IsRestrictCategoriesEnabled ? 1 : 0);
                sparkCmd.Parameters.AddWithValue("@restrictedCats", c.RestrictedCategoriesCsv ?? "");
                sparkCmd.Parameters.AddWithValue("@variantsEnabled", c.IsVariantsEnabled ? 1 : 0);
                sparkCmd.Parameters.AddWithValue("@autoPrint", c.IsAutomaticReceiptPrintingEnabled ? 1 : 0);
                sparkCmd.Parameters.AddWithValue("@odooId", c.OdooId);
                await sparkCmd.ExecuteNonQueryAsync();
                updated++;
                continue;
            }

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = transaction;

            if (existingId != null && existingId != DBNull.Value)
            {
                cmd.CommandText = """
                    UPDATE PosConfigs
                    SET Name = @name, CurrentSessionId = @sessId, CurrentSessionName = @sessName,
                        CurrentSessionState = @state, LastClosingDate = @closingDate,
                        LastClosingBalance = @closingBal, OpeningBalance = @openBal,
                        ActiveUserId = @userId, ActiveUserName = @userName, ActiveUserAvatar = @avatar,
                        RecentSalesPointsJson = @salesPoints,
                        SelectedPaymentMethodsCsv = @payMethods,
                        IsOneClickPaymentEnabled = @oneClickEnabled,
                        OneClickPaymentMethodsCsv = @oneClickMethods,
                        IsRestrictCategoriesEnabled = @restrictCatEnabled,
                        RestrictedCategoriesCsv = @restrictedCats,
                        IsVariantsEnabled = @variantsEnabled,
                        IsAutomaticReceiptPrintingEnabled = @autoPrint,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE OdooId = @odooId;
                    """;
                updated++;
            }
            else
            {
                cmd.CommandText = """
                    INSERT INTO PosConfigs (OdooId, Name, CurrentSessionId, CurrentSessionName, CurrentSessionState,
                                           LastClosingDate, LastClosingBalance, OpeningBalance,
                                           ActiveUserId, ActiveUserName, ActiveUserAvatar, RecentSalesPointsJson,
                                           SelectedPaymentMethodsCsv, IsOneClickPaymentEnabled, OneClickPaymentMethodsCsv,
                                           IsRestrictCategoriesEnabled, RestrictedCategoriesCsv, IsVariantsEnabled,
                                           IsAutomaticReceiptPrintingEnabled)
                    VALUES (@odooId, @name, @sessId, @sessName, @state,
                            @closingDate, @closingBal, @openBal,
                            @userId, @userName, @avatar, @salesPoints,
                            @payMethods, @oneClickEnabled, @oneClickMethods,
                            @restrictCatEnabled, @restrictedCats, @variantsEnabled,
                            @autoPrint);
                    """;
                imported++;
            }

            cmd.Parameters.AddWithValue("@odooId", c.OdooId);
            cmd.Parameters.AddWithValue("@name", c.Name);
            cmd.Parameters.AddWithValue("@sessId", (object?)c.CurrentSessionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@sessName", (object?)c.CurrentSessionName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@state", c.CurrentSessionState ?? "closed");
            cmd.Parameters.AddWithValue("@closingDate", c.LastClosingDate.HasValue ? c.LastClosingDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value);
            cmd.Parameters.AddWithValue("@closingBal", (double)c.LastClosingBalance);
            cmd.Parameters.AddWithValue("@openBal", (double)c.OpeningBalance);
            cmd.Parameters.AddWithValue("@userId", (object?)c.ActiveUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@userName", (object?)c.ActiveUserName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@avatar", (object?)c.ActiveUserAvatar ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@salesPoints", c.RecentSalesPointsJson ?? "[]");
            cmd.Parameters.AddWithValue("@payMethods", c.SelectedPaymentMethodsCsv ?? "Cash,Card,Customer Account");
            cmd.Parameters.AddWithValue("@oneClickEnabled", c.IsOneClickPaymentEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@oneClickMethods", c.OneClickPaymentMethod ?? "Cash");
            cmd.Parameters.AddWithValue("@restrictCatEnabled", c.IsRestrictCategoriesEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@restrictedCats", c.RestrictedCategoriesCsv ?? "");
            cmd.Parameters.AddWithValue("@variantsEnabled", c.IsVariantsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@autoPrint", c.IsAutomaticReceiptPrintingEnabled ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

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

    public async Task SavePosConfigSettingsAsync(PosConfigRecord config)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE PosConfigs
            SET IsPreventLineMergingEnabled = @preventMerge,
                IsPrintLastReceiptEnabled = @printLast,
                IsCustomOrderReceiptsEnabled = @customReceiptEnabled,
                CustomOrderReceiptDesignId = @customReceiptId,
                IsHideOpeningCashControlEnabled = @hideOpening,
                DefaultOpeningCashAmount = @defaultCash,
                IsSecondReceiptEnabled = @secondReceiptEnabled,
                SecondReceiptDesignId = @secondReceiptId,
                IsHideClosingRegisterEnabled = @hideClosing,
                CustomSalesReceiptDesignId = @customSalesReceiptId,
                IsHideReviewOrdersEnabled = @hideReviewOrders,
                NumbersFirstOrProducts = @numbersFirst,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @id OR OdooId = @odooId;
            """;

        cmd.Parameters.AddWithValue("@preventMerge", config.IsPreventLineMergingEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@printLast", config.IsPrintLastReceiptEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@customReceiptEnabled", config.IsCustomOrderReceiptsEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@customReceiptId", (object?)config.CustomOrderReceiptDesignId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hideOpening", config.IsHideOpeningCashControlEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@defaultCash", (double)config.DefaultOpeningCashAmount);
        cmd.Parameters.AddWithValue("@secondReceiptEnabled", config.IsSecondReceiptEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@secondReceiptId", (object?)config.SecondReceiptDesignId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hideClosing", config.IsHideClosingRegisterEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@customSalesReceiptId", (object?)config.CustomSalesReceiptDesignId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hideReviewOrders", config.IsHideReviewOrdersEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@numbersFirst", config.NumbersFirstOrProducts ?? "products");
        cmd.Parameters.AddWithValue("@id", config.Id);
        cmd.Parameters.AddWithValue("@odooId", config.OdooId);

        await cmd.ExecuteNonQueryAsync();
    }

    // =========================================================================
    // 12B. RECEIPT DESIGNS REPOSITORY (pos.receipt.design)
    // =========================================================================
    public async Task<List<ReceiptDesignRecord>> LoadAllReceiptDesignsAsync()
    {
        var list = new List<ReceiptDesignRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT d.Id, d.Name, COALESCE(d.OpenCashDrawerOnly, 0), COALESCE(d.ReceiptXml, ''), COALESCE(d.UpdatedAt, ''),
                   d.CompanyId, c.Name AS CompanyName
            FROM PosReceiptDesigns d
            LEFT JOIN Companies c ON d.CompanyId = c.OdooId OR d.CompanyId = c.Id
            ORDER BY d.Id ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ReceiptDesignRecord
            {
                Id = reader.GetInt64(0),
                Name = reader.GetString(1),
                OpenCashDrawerOnly = reader.GetInt32(2) != 0,
                ReceiptXml = reader.GetString(3),
                UpdatedAt = reader.GetString(4),
                CompanyId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
                CompanyName = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task SaveReceiptDesignAsync(ReceiptDesignRecord design)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();

        if (design.Id > 0)
        {
            cmd.CommandText = """
                UPDATE PosReceiptDesigns
                SET Name = @name,
                    OpenCashDrawerOnly = @openDrawerOnly,
                    ReceiptXml = @xml,
                    CompanyId = @companyId,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @id;
                """;
            cmd.Parameters.AddWithValue("@id", design.Id);
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO PosReceiptDesigns (Name, OpenCashDrawerOnly, ReceiptXml, CompanyId, UpdatedAt)
                VALUES (@name, @openDrawerOnly, @xml, @companyId, CURRENT_TIMESTAMP);
                SELECT last_insert_rowid();
                """;
        }

        cmd.Parameters.AddWithValue("@name", string.IsNullOrWhiteSpace(design.Name) ? "New Receipt Design" : design.Name.Trim());
        cmd.Parameters.AddWithValue("@openDrawerOnly", design.OpenCashDrawerOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("@xml", design.ReceiptXml ?? string.Empty);
        cmd.Parameters.AddWithValue("@companyId", (object?)design.CompanyId ?? DBNull.Value);

        if (design.Id > 0)
        {
            await cmd.ExecuteNonQueryAsync();
        }
        else
        {
            var newId = await cmd.ExecuteScalarAsync();
            if (newId != null && long.TryParse(newId.ToString(), out long generatedId))
            {
                design.Id = generatedId;
            }
        }
    }

    public async Task DeleteReceiptDesignAsync(long id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PosReceiptDesigns WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // =========================================================================
    // 12C. GENERAL & PRINTER SETTINGS REPOSITORY
    // =========================================================================
    public async Task<GeneralSettingsRecord> GetGeneralSettingsAsync()
    {
        var record = new GeneralSettingsRecord();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT g.ScreenMode, g.DefaultCompanyId, c.Name AS CompanyName,
                   g.DefaultPrinterName, g.PaperWidth, g.AutoPrintReceipt,
                   g.CashDrawerPulse, g.PrintHeaderLogo, g.PrinterEncoding
            FROM GeneralSettings g
            LEFT JOIN Companies c ON g.DefaultCompanyId = c.OdooId OR g.DefaultCompanyId = c.Id
            WHERE g.Id = 1;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            record.ScreenMode = reader.IsDBNull(0) ? "Window" : reader.GetString(0);
            record.DefaultCompanyId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            record.DefaultCompanyName = reader.IsDBNull(2) ? null : reader.GetString(2);
            record.DefaultPrinterName = reader.IsDBNull(3) ? "" : reader.GetString(3);
            record.PaperWidth = reader.IsDBNull(4) ? "80mm" : reader.GetString(4);
            record.AutoPrintReceipt = !reader.IsDBNull(5) && reader.GetInt32(5) != 0;
            record.CashDrawerPulse = !reader.IsDBNull(6) && reader.GetInt32(6) != 0;
            record.PrintHeaderLogo = reader.IsDBNull(7) || reader.GetInt32(7) != 0;
            record.PrinterEncoding = reader.IsDBNull(8) ? "UTF-8" : reader.GetString(8);
        }
        else
        {
            // Seed default general settings row
            record.DefaultPrinterName = PosPrinterService.GetDefaultPrinter();
            await SaveGeneralSettingsAsync(record);
        }

        return record;
    }

    public async Task SaveGeneralSettingsAsync(GeneralSettingsRecord settings)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO GeneralSettings (Id, ScreenMode, DefaultCompanyId, DefaultPrinterName, PaperWidth,
                                         AutoPrintReceipt, CashDrawerPulse, PrintHeaderLogo, PrinterEncoding, UpdatedAt)
            VALUES (1, @screenMode, @companyId, @printerName, @paperWidth,
                    @autoPrint, @drawerPulse, @printLogo, @encoding, CURRENT_TIMESTAMP)
            ON CONFLICT(Id) DO UPDATE SET
                ScreenMode = @screenMode,
                DefaultCompanyId = @companyId,
                DefaultPrinterName = @printerName,
                PaperWidth = @paperWidth,
                AutoPrintReceipt = @autoPrint,
                CashDrawerPulse = @drawerPulse,
                PrintHeaderLogo = @printLogo,
                PrinterEncoding = @encoding,
                UpdatedAt = CURRENT_TIMESTAMP;
            """;

        cmd.Parameters.AddWithValue("@screenMode", settings.ScreenMode ?? "Window");
        cmd.Parameters.AddWithValue("@companyId", (object?)settings.DefaultCompanyId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@printerName", settings.DefaultPrinterName ?? "");
        cmd.Parameters.AddWithValue("@paperWidth", settings.PaperWidth ?? "80mm");
        cmd.Parameters.AddWithValue("@autoPrint", settings.AutoPrintReceipt ? 1 : 0);
        cmd.Parameters.AddWithValue("@drawerPulse", settings.CashDrawerPulse ? 1 : 0);
        cmd.Parameters.AddWithValue("@printLogo", settings.PrintHeaderLogo ? 1 : 0);
        cmd.Parameters.AddWithValue("@encoding", settings.PrinterEncoding ?? "UTF-8");

        await cmd.ExecuteNonQueryAsync();
    }

    // =========================================================================
    // 13. POS REGISTER & SELLING WORKFLOW HELPER METHODS + OFFLINE SYNC QUEUE
    // =========================================================================
    public async Task<(long LocalSessionId, int? SessionOdooId, string SessionName)> UpdateSessionOpeningControlAsync(
        int configId,
        int? sessionId,
        decimal openingBalance,
        string? openingNote,
        string? configName = null,
        int? userId = null,
        string? cashierName = null,
        bool enqueueForOdooSync = true)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        string resolvedConfigName = configName ?? "Point of Sale";
        if (string.IsNullOrWhiteSpace(configName))
        {
            await using var cfgNameCmd = connection.CreateCommand();
            cfgNameCmd.Transaction = transaction;
            cfgNameCmd.CommandText = "SELECT Name FROM PosConfigs WHERE OdooId = @cfgId OR Id = @cfgId LIMIT 1;";
            cfgNameCmd.Parameters.AddWithValue("@cfgId", configId);
            var nObj = await cfgNameCmd.ExecuteScalarAsync();
            if (nObj is string s && !string.IsNullOrWhiteSpace(s)) resolvedConfigName = s;
        }

        long localSessId = 0;
        int? odooSessId = null;
        string sessName = string.Empty;

        // ONLY match an existing session if it is NOT closed for this config
        await using (var findCmd = connection.CreateCommand())
        {
            findCmd.Transaction = transaction;
            findCmd.CommandText = """
                SELECT Id, OdooId, Name
                FROM PosSessions
                WHERE PosConfigId = @cfgId AND LOWER(COALESCE(State, 'opened')) != 'closed'
                ORDER BY Id DESC
                LIMIT 1;
                """;
            findCmd.Parameters.AddWithValue("@cfgId", configId);
            await using var r = await findCmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                localSessId = r.GetInt64(0);
                if (!r.IsDBNull(1)) odooSessId = r.GetInt32(1);
                if (!r.IsDBNull(2) && !string.IsNullOrWhiteSpace(r.GetString(2))) sessName = r.GetString(2);
            }
        }

        if (localSessId > 0)
        {
            await using var cmdSession = connection.CreateCommand();
            cmdSession.Transaction = transaction;
            cmdSession.CommandText = """
                UPDATE PosSessions
                SET OpeningBalance = @openBal,
                    OpeningNote = @note,
                    State = 'opened',
                    StartAt = COALESCE(StartAt, @now),
                    UserName = COALESCE(@userName, UserName),
                    IsSynced = @synced,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @id;
                """;
            cmdSession.Parameters.AddWithValue("@openBal", (double)openingBalance);
            cmdSession.Parameters.AddWithValue("@note", openingNote ?? "");
            cmdSession.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmdSession.Parameters.AddWithValue("@userName", (object?)cashierName ?? DBNull.Value);
            cmdSession.Parameters.AddWithValue("@synced", enqueueForOdooSync ? 0 : 1);
            cmdSession.Parameters.AddWithValue("@id", localSessId);
            await cmdSession.ExecuteNonQueryAsync();
        }
        else
        {
            // Compute highest sequence number for this config name to ensure strictly sequential naming
            long maxSeq = 0;
            await using (var checkCmd = connection.CreateCommand())
            {
                checkCmd.Transaction = transaction;
                checkCmd.CommandText = """
                    SELECT Name FROM PosSessions
                    WHERE PosConfigId = @cfgId OR PosConfigName = @cfgName
                    ORDER BY Id DESC LIMIT 50;
                    """;
                checkCmd.Parameters.AddWithValue("@cfgId", configId);
                checkCmd.Parameters.AddWithValue("@cfgName", resolvedConfigName);
                await using var rSeq = await checkCmd.ExecuteReaderAsync();
                while (await rSeq.ReadAsync())
                {
                    if (!rSeq.IsDBNull(0))
                    {
                        string nameStr = rSeq.GetString(0);
                        int slashIdx = nameStr.LastIndexOf('/');
                        if (slashIdx >= 0 && slashIdx + 1 < nameStr.Length)
                        {
                            string part = nameStr.Substring(slashIdx + 1);
                            if (long.TryParse(part, out long parsedVal) && parsedVal > maxSeq)
                            {
                                maxSeq = parsedVal;
                            }
                        }
                    }
                }
            }

            if (maxSeq <= 0)
            {
                await using var seqCmd = connection.CreateCommand();
                seqCmd.Transaction = transaction;
                seqCmd.CommandText = "SELECT COUNT(*) + 1 FROM PosSessions WHERE PosConfigId = @cfgId OR PosConfigName = @cfgName;";
                seqCmd.Parameters.AddWithValue("@cfgId", configId);
                seqCmd.Parameters.AddWithValue("@cfgName", resolvedConfigName);
                maxSeq = Convert.ToInt64(await seqCmd.ExecuteScalarAsync() ?? 1L);
            }
            else
            {
                maxSeq += 1;
            }

            sessName = $"{resolvedConfigName}/{maxSeq:D5}";

            await using var insSession = connection.CreateCommand();
            insSession.Transaction = transaction;
            insSession.CommandText = """
                INSERT INTO PosSessions (OdooId, Name, PosConfigId, PosConfigName, UserId, UserName,
                                        StartAt, State, OpeningBalance, OpeningNote, OrdersCount, TotalPaymentsAmount, CashInOutTotal, IsSynced)
                VALUES (NULL, @name, @cfgId, @cfgName, @userId, @userName,
                        @now, 'opened', @openBal, @note, 0, 0.0, 0.0, @synced);
                SELECT last_insert_rowid();
                """;
            insSession.Parameters.AddWithValue("@name", sessName);
            insSession.Parameters.AddWithValue("@cfgId", configId);
            insSession.Parameters.AddWithValue("@cfgName", resolvedConfigName);
            insSession.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
            insSession.Parameters.AddWithValue("@userName", cashierName ?? "Mitchell Admin");
            insSession.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            insSession.Parameters.AddWithValue("@openBal", (double)openingBalance);
            insSession.Parameters.AddWithValue("@note", openingNote ?? "");
            insSession.Parameters.AddWithValue("@synced", enqueueForOdooSync ? 0 : 1);

            localSessId = Convert.ToInt64(await insSession.ExecuteScalarAsync() ?? 0L);
            odooSessId = null;
        }

        int effectiveSessionRef = odooSessId ?? (int)localSessId;

        await using (var cmdConfig = connection.CreateCommand())
        {
            cmdConfig.Transaction = transaction;
            cmdConfig.CommandText = """
                UPDATE PosConfigs
                SET CurrentSessionId = @sessRef,
                    CurrentSessionName = @sessName,
                    OpeningBalance = @openBal,
                    CurrentSessionState = 'opened',
                    ActiveUserId = COALESCE(@userId, ActiveUserId),
                    ActiveUserName = COALESCE(@userName, ActiveUserName),
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE OdooId = @cfgId OR Id = @cfgId;
                """;
            cmdConfig.Parameters.AddWithValue("@sessRef", effectiveSessionRef);
            cmdConfig.Parameters.AddWithValue("@sessName", sessName);
            cmdConfig.Parameters.AddWithValue("@openBal", (double)openingBalance);
            cmdConfig.Parameters.AddWithValue("@userId", (object?)userId ?? DBNull.Value);
            cmdConfig.Parameters.AddWithValue("@userName", (object?)cashierName ?? DBNull.Value);
            cmdConfig.Parameters.AddWithValue("@cfgId", configId);
            await cmdConfig.ExecuteNonQueryAsync();
        }

        if (enqueueForOdooSync)
        {
            var payload = new OpenSessionSyncPayload(
                configId,
                resolvedConfigName,
                localSessId,
                odooSessId,
                openingBalance,
                openingNote,
                userId,
                cashierName ?? "Mitchell Admin",
                DateTime.Now
            );

            await EnqueueSyncItemInternalAsync(
                connection,
                transaction,
                "OPEN_SESSION",
                $"session-{localSessId}-{DateTime.UtcNow.Ticks}",
                localSessId,
                configId,
                odooSessId,
                JsonSerializer.Serialize(payload)
            );
        }

        await transaction.CommitAsync();
        return (localSessId, odooSessId, sessName);
    }

    public async Task RecordCashInOutAsync(
        int configId,
        int? sessionId,
        decimal amount,
        string reason,
        bool enqueueForOdooSync = true)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        long? localSessId = null;
        int? odooSessId = sessionId;

        await using (var findCmd = connection.CreateCommand())
        {
            findCmd.Transaction = transaction;
            findCmd.CommandText = """
                SELECT Id, OdooId FROM PosSessions
                WHERE (@sessId IS NOT NULL AND (OdooId = @sessId OR Id = @sessId))
                   OR (PosConfigId = @cfgId AND LOWER(State) != 'closed')
                ORDER BY Id DESC LIMIT 1;
                """;
            findCmd.Parameters.AddWithValue("@sessId", (object?)sessionId ?? DBNull.Value);
            findCmd.Parameters.AddWithValue("@cfgId", configId);
            await using var r = await findCmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                localSessId = r.GetInt64(0);
                if (!r.IsDBNull(1)) odooSessId = r.GetInt32(1);
            }
        }

        if (localSessId.HasValue)
        {
            await using var updCmd = connection.CreateCommand();
            updCmd.Transaction = transaction;
            updCmd.CommandText = """
                UPDATE PosSessions
                SET CashInOutTotal = COALESCE(CashInOutTotal, 0.0) + @amt,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @id;
                """;
            updCmd.Parameters.AddWithValue("@amt", (double)amount);
            updCmd.Parameters.AddWithValue("@id", localSessId.Value);
            await updCmd.ExecuteNonQueryAsync();
        }

        if (enqueueForOdooSync)
        {
            var payload = new CashInOutSyncPayload(
                configId,
                odooSessId,
                localSessId,
                amount,
                reason ?? "",
                DateTime.Now
            );

            await EnqueueSyncItemInternalAsync(
                connection,
                transaction,
                "CASH_IN_OUT",
                $"cash-{Guid.NewGuid():N}",
                localSessId,
                configId,
                odooSessId,
                JsonSerializer.Serialize(payload)
            );
        }

        await transaction.CommitAsync();
    }

    public async Task<decimal> GetSessionCashInOutTotalAsync(int sessionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(CashInOutTotal, 0.0)
            FROM PosSessions
            WHERE OdooId = @sessId OR Id = @sessId
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@sessId", sessionId);
        var res = await cmd.ExecuteScalarAsync();
        return res != null && res != DBNull.Value ? Convert.ToDecimal(res) : 0.00m;
    }

    public async Task<decimal> GetSessionOpeningBalanceAsync(int sessionId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(OpeningBalance, 0.0)
            FROM PosSessions
            WHERE OdooId = @sessId OR Id = @sessId
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@sessId", sessionId);
        var res = await cmd.ExecuteScalarAsync();
        return res != null && res != DBNull.Value ? Convert.ToDecimal(res) : 0.00m;
    }

    public async Task CloseSessionAsync(
        int configId,
        int sessionId,
        decimal closingBalance,
        string? closingNote,
        bool enqueueForOdooSync = true)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        long? localSessId = null;
        int? odooSessId = null;
        decimal openingBalance = 0m;
        DateTime? startAt = null;

        await using (var findCmd = connection.CreateCommand())
        {
            findCmd.Transaction = transaction;
            findCmd.CommandText = """
                SELECT Id, OdooId, COALESCE(OpeningBalance, 0.0), StartAt FROM PosSessions
                WHERE (PosConfigId = @cfgId AND LOWER(COALESCE(State, 'opened')) != 'closed')
                   OR Id = @sessId
                   OR OdooId = @sessId
                ORDER BY CASE WHEN (PosConfigId = @cfgId AND LOWER(COALESCE(State, 'opened')) != 'closed') THEN 0 ELSE 1 END, Id DESC
                LIMIT 1;
                """;
            findCmd.Parameters.AddWithValue("@cfgId", configId);
            findCmd.Parameters.AddWithValue("@sessId", sessionId);
            await using var r = await findCmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                localSessId = r.GetInt64(0);
                if (!r.IsDBNull(1)) odooSessId = r.GetInt32(1);
                openingBalance = Convert.ToDecimal(r.GetDouble(2));
                if (!r.IsDBNull(3))
                {
                    if (DateTime.TryParse(r.GetString(3), out var dt)) startAt = dt;
                }
            }
        }

        if (openingBalance == 0m)
        {
            await using var cfgCmd = connection.CreateCommand();
            cfgCmd.Transaction = transaction;
            cfgCmd.CommandText = "SELECT COALESCE(OpeningBalance, 0.0) FROM PosConfigs WHERE OdooId = @cfgId OR Id = @cfgId LIMIT 1;";
            cfgCmd.Parameters.AddWithValue("@cfgId", configId);
            var o = await cfgCmd.ExecuteScalarAsync();
            if (o != null && o != DBNull.Value) openingBalance = Convert.ToDecimal(o);
        }

        if (!odooSessId.HasValue && sessionId > 0)
        {
            odooSessId = sessionId;
        }

        string nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        await using (var cmdSession = connection.CreateCommand())
        {
            cmdSession.Transaction = transaction;
            cmdSession.CommandText = """
                UPDATE PosSessions
                SET ClosingBalance = @closeBal,
                    ClosingNote = @note,
                    StopAt = @now,
                    State = 'closed',
                    IsSynced = @synced,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE (@localId IS NOT NULL AND Id = @localId)
                   OR (PosConfigId = @cfgId AND LOWER(COALESCE(State, 'opened')) != 'closed');
                """;
            cmdSession.Parameters.AddWithValue("@closeBal", (double)closingBalance);
            cmdSession.Parameters.AddWithValue("@note", closingNote ?? "");
            cmdSession.Parameters.AddWithValue("@now", nowStr);
            cmdSession.Parameters.AddWithValue("@synced", enqueueForOdooSync ? 0 : 1);
            cmdSession.Parameters.AddWithValue("@localId", (object?)localSessId ?? DBNull.Value);
            cmdSession.Parameters.AddWithValue("@cfgId", configId);
            await cmdSession.ExecuteNonQueryAsync();
        }

        await using (var cmdConfig = connection.CreateCommand())
        {
            cmdConfig.Transaction = transaction;
            cmdConfig.CommandText = """
                UPDATE PosConfigs
                SET CurrentSessionState = 'closed',
                    LastClosingBalance = @closeBal,
                    LastClosingDate = @now,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE OdooId = @cfgId OR Id = @cfgId;
                """;
            cmdConfig.Parameters.AddWithValue("@closeBal", (double)closingBalance);
            cmdConfig.Parameters.AddWithValue("@now", nowStr);
            cmdConfig.Parameters.AddWithValue("@cfgId", configId);
            await cmdConfig.ExecuteNonQueryAsync();
        }

        // Switch all paid orders for this session from 'paid' to 'posted'
        await using (var cmdOrders = connection.CreateCommand())
        {
            cmdOrders.Transaction = transaction;
            cmdOrders.CommandText = """
                UPDATE PosOrders
                SET State = 'posted',
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE ((@localId IS NOT NULL AND SessionId = @localId)
                    OR (@sessId IS NOT NULL AND (SessionOdooId = @sessId OR SessionId = @sessId)))
                  AND LOWER(State) = 'paid';
                """;
            cmdOrders.Parameters.AddWithValue("@localId", (object?)localSessId ?? DBNull.Value);
            cmdOrders.Parameters.AddWithValue("@sessId", sessionId);
            await cmdOrders.ExecuteNonQueryAsync();
        }

        if (enqueueForOdooSync)
        {
            var payload = new CloseSessionSyncPayload(
                configId,
                odooSessId,
                localSessId ?? sessionId,
                closingBalance,
                closingNote,
                DateTime.Now,
                openingBalance,
                startAt
            );

            await EnqueueSyncItemInternalAsync(
                connection,
                transaction,
                "CLOSE_SESSION",
                $"close-session-{localSessId ?? sessionId}-{DateTime.UtcNow.Ticks}",
                localSessId ?? sessionId,
                configId,
                odooSessId,
                JsonSerializer.Serialize(payload)
            );
        }

        await transaction.CommitAsync();
    }

    public async Task<int> GetSessionDraftOrdersCountAsync(int sessionId, string? shopName = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM PosOrders
            WHERE (SessionId = @sessId OR SessionOdooId = @sessId OR (@shop IS NOT NULL AND PosConfigName = @shop))
              AND LOWER(State) IN ('draft', 'ongoing', 'new');
            """;
        cmd.Parameters.AddWithValue("@sessId", sessionId);
        cmd.Parameters.AddWithValue("@shop", (object?)shopName ?? DBNull.Value);
        var res = await cmd.ExecuteScalarAsync();
        return res is long l ? (int)l : 0;
    }

    public async Task CancelAllSessionDraftOrdersAsync(int sessionId, string? shopName = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            DELETE FROM PosOrderLines
            WHERE OrderId IN (
                SELECT Id FROM PosOrders
                WHERE (SessionId = @sessId OR SessionOdooId = @sessId OR (@shop IS NOT NULL AND PosConfigName = @shop))
                  AND LOWER(State) IN ('draft', 'ongoing', 'new')
            );
            DELETE FROM PosOrders
            WHERE (SessionId = @sessId OR SessionOdooId = @sessId OR (@shop IS NOT NULL AND PosConfigName = @shop))
              AND LOWER(State) IN ('draft', 'ongoing', 'new');
            """;
        cmd.Parameters.AddWithValue("@sessId", sessionId);
        cmd.Parameters.AddWithValue("@shop", (object?)shopName ?? DBNull.Value);
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

    public async Task<long> SavePosOrderAsync(
        PosOrderRecord order,
        List<PosOrderLineRecord> lines,
        List<PosPaymentRecord> payments,
        int configOdooId = 1,
        bool isToInvoice = false,
        int? partnerId = null,
        bool enqueueForOdooSync = true)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        if (string.IsNullOrWhiteSpace(order.Uuid))
        {
            order.Uuid = Guid.NewGuid().ToString();
        }

        bool isPaidOrder = string.Equals(order.State, "paid", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(order.State, "done", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(order.State, "invoiced", StringComparison.OrdinalIgnoreCase);

        bool shouldEnqueue = isPaidOrder && enqueueForOdooSync;
        order.IsSynced = !shouldEnqueue;

        long orderId = order.Id;
        if (orderId <= 0 && !string.IsNullOrWhiteSpace(order.Uuid))
        {
            await using var findByUuidCmd = connection.CreateCommand();
            findByUuidCmd.Transaction = transaction;
            findByUuidCmd.CommandText = "SELECT Id FROM PosOrders WHERE Uuid = @uuid LIMIT 1;";
            findByUuidCmd.Parameters.AddWithValue("@uuid", order.Uuid);
            var existingObj = await findByUuidCmd.ExecuteScalarAsync();
            if (existingObj != null && existingObj != DBNull.Value)
            {
                orderId = Convert.ToInt64(existingObj);
                order.Id = orderId;
            }
        }

        if (orderId > 0)
        {
            await using var updateCmd = connection.CreateCommand();
            updateCmd.Transaction = transaction;
            updateCmd.CommandText = """
                UPDATE PosOrders
                SET Uuid = @uuid, Name = @name, PosReference = @posRef, TrackingNumber = @trackNum,
                    SessionId = @sessId, SessionOdooId = @sessOdooId, SessionName = @sessName,
                    PosConfigName = @cfgName, DateOrder = @dateOrder,
                    CashierId = @cashierId, CashierName = @cashierName,
                    PartnerId = @partnerId, CustomerName = @customerName, IsToInvoice = @toInvoice,
                    AmountTotal = @total, AmountTax = @tax, AmountPaid = @paid,
                    State = @state, Note = @note, IsSynced = @isSynced, UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @id;
                """;
            updateCmd.Parameters.AddWithValue("@id", orderId);
            updateCmd.Parameters.AddWithValue("@uuid", order.Uuid);
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
            updateCmd.Parameters.AddWithValue("@partnerId", (object?)partnerId ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("@customerName", order.CustomerName ?? "");
            updateCmd.Parameters.AddWithValue("@toInvoice", isToInvoice ? 1 : 0);
            updateCmd.Parameters.AddWithValue("@total", (double)order.AmountTotal);
            updateCmd.Parameters.AddWithValue("@tax", (double)order.AmountTax);
            updateCmd.Parameters.AddWithValue("@paid", (double)order.AmountPaid);
            updateCmd.Parameters.AddWithValue("@state", order.State);
            updateCmd.Parameters.AddWithValue("@note", order.Note ?? "");
            updateCmd.Parameters.AddWithValue("@isSynced", order.IsSynced ? 1 : 0);
            await updateCmd.ExecuteNonQueryAsync();

            await using var clearLinesCmd = connection.CreateCommand();
            clearLinesCmd.Transaction = transaction;
            clearLinesCmd.CommandText = """
                DELETE FROM PosOrderLines WHERE OrderId = @orderId;
                DELETE FROM PosPayments WHERE OrderId = @orderId;
                """;
            clearLinesCmd.Parameters.AddWithValue("@orderId", orderId);
            await clearLinesCmd.ExecuteNonQueryAsync();
        }
        else
        {
            await using var insertCmd = connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO PosOrders (Uuid, Name, PosReference, TrackingNumber, SessionId, SessionOdooId, SessionName, PosConfigName,
                                       DateOrder, CashierId, CashierName, PartnerId, CustomerName, IsToInvoice,
                                       FiscalPosition, AmountTotal, AmountTax, AmountPaid,
                                       Margin, MarginRatio, State, IsEdited, Company, Note, IsSynced)
                VALUES (@uuid, @name, @posRef, @trackNum, @sessId, @sessOdooId, @sessName, @cfgName,
                        @dateOrder, @cashierId, @cashierName, @partnerId, @customerName, @toInvoice,
                        @fiscal, @total, @tax, @paid,
                        @margin, @marginRatio, @state, @isEdited, @company, @note, @isSynced);
                SELECT last_insert_rowid();
                """;
            insertCmd.Parameters.AddWithValue("@uuid", order.Uuid);
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
            insertCmd.Parameters.AddWithValue("@partnerId", (object?)partnerId ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@customerName", order.CustomerName ?? "");
            insertCmd.Parameters.AddWithValue("@toInvoice", isToInvoice ? 1 : 0);
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
            insertCmd.Parameters.AddWithValue("@isSynced", order.IsSynced ? 1 : 0);
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
                INSERT INTO PosPayments (OrderId, OrderName, SessionId, SessionOdooId, SessionName, CashierName,
                                         PaymentMethodId, PaymentMethodName, Amount, CurrencyName, PaymentDate, IsSynced)
                VALUES (@orderId, @orderName, @sessId, @sessOdooId, @sessName, @cashier,
                        @methodId, @method, @amount, @curr, @date, @isSynced);
                """;
            payCmd.Parameters.AddWithValue("@orderId", orderId);
            payCmd.Parameters.AddWithValue("@orderName", order.Name);
            payCmd.Parameters.AddWithValue("@sessId", (object?)order.SessionId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@sessOdooId", (object?)order.SessionOdooId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@sessName", order.SessionName);
            payCmd.Parameters.AddWithValue("@cashier", order.CashierName);
            payCmd.Parameters.AddWithValue("@methodId", (object?)p.PaymentMethodId ?? DBNull.Value);
            payCmd.Parameters.AddWithValue("@method", p.PaymentMethodName ?? "Cash");
            payCmd.Parameters.AddWithValue("@amount", (double)p.Amount);
            payCmd.Parameters.AddWithValue("@curr", p.CurrencyName ?? "MAD");
            payCmd.Parameters.AddWithValue("@date", p.PaymentDate.HasValue ? p.PaymentDate.Value.ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            payCmd.Parameters.AddWithValue("@isSynced", order.IsSynced ? 1 : 0);
            await payCmd.ExecuteNonQueryAsync();
        }

        if (isPaidOrder && (order.SessionOdooId.HasValue || order.SessionId.HasValue))
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

        if (shouldEnqueue)
        {
            var syncLines = lines.Select(l => new PosOrderLineSyncPayload(
                l.ProductId,
                l.ProductName,
                l.Qty,
                l.PriceUnit,
                l.Discount,
                l.Taxes ?? "15%",
                15.0m,
                l.PriceSubtotal,
                l.PriceSubtotalIncl,
                null
            )).ToList();

            var syncPayments = payments.Select(p => new PosPaymentSyncPayload(
                p.PaymentMethodId,
                p.PaymentMethodName ?? "Cash",
                p.Amount,
                p.PaymentDate ?? DateTime.Now
            )).ToList();

            var orderPayload = new CreateOrderSyncPayload(
                order.Uuid,
                orderId,
                configOdooId,
                order.SessionOdooId,
                order.SessionId,
                order.Name,
                order.PosReference,
                order.TrackingNumber,
                order.DateOrder ?? DateTime.Now,
                order.CashierId,
                order.CashierName,
                partnerId,
                order.CustomerName ?? "",
                isToInvoice,
                order.AmountTotal,
                order.AmountTax,
                order.AmountPaid,
                order.Note ?? "",
                syncLines,
                syncPayments
            );

            await EnqueueSyncItemInternalAsync(
                connection,
                transaction,
                "CREATE_ORDER",
                order.Uuid,
                orderId,
                configOdooId,
                order.SessionOdooId,
                JsonSerializer.Serialize(orderPayload)
            );
        }

        await transaction.CommitAsync();
        return orderId;
    }

    // =========================================================================
    // 14. SEQUENTIAL OFFLINE SYNC QUEUE REPOSITORY METHODS
    // =========================================================================
    private static async Task EnqueueSyncItemInternalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string operationType,
        string entityUuid,
        long? localEntityId,
        int? posConfigOdooId,
        int? sessionOdooId,
        string payloadJson)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO PendingSyncQueue (OperationType, EntityUuid, LocalEntityId, PosConfigOdooId, SessionOdooId, PayloadJson, Status, RetryCount, CreatedAt)
            VALUES (@op, @uuid, @localId, @cfgId, @sessId, @payload, 'Pending', 0, @now);
            """;
        cmd.Parameters.AddWithValue("@op", operationType);
        cmd.Parameters.AddWithValue("@uuid", entityUuid);
        cmd.Parameters.AddWithValue("@localId", (object?)localEntityId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cfgId", (object?)posConfigOdooId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sessId", (object?)sessionOdooId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@payload", payloadJson);
        cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<PendingSyncItemRecord>> GetPendingSyncQueueAsync()
    {
        var items = new List<PendingSyncItemRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, OperationType, EntityUuid, LocalEntityId, PosConfigOdooId, SessionOdooId,
                   PayloadJson, Status, RetryCount, LastError, CreatedAt
            FROM PendingSyncQueue
            WHERE Status != 'Completed'
            ORDER BY Id ASC;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new PendingSyncItemRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? "Pending" : reader.GetString(7),
                reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? "" : reader.GetString(10)
            ));
        }

        return items;
    }

    public async Task<int> GetPendingSyncCountAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM PendingSyncQueue WHERE Status != 'Completed';";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    public async Task MarkSyncQueueItemCompletedAsync(long queueId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM PendingSyncQueue WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", queueId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task MarkSyncQueueItemFailedAsync(long queueId, string errorMessage)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE PendingSyncQueue
            SET Status = 'Failed',
                RetryCount = RetryCount + 1,
                LastError = @err
            WHERE Id = @id;
            """;
        cmd.Parameters.AddWithValue("@err", errorMessage);
        cmd.Parameters.AddWithValue("@id", queueId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task BindLocalSessionToOdooSessionAsync(
        long localSessionId,
        int configOdooId,
        int odooSessionId,
        string? odooSessionName)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var updSess = connection.CreateCommand())
        {
            updSess.Transaction = transaction;
            updSess.CommandText = """
                UPDATE PosSessions
                SET OdooId = @odooId,
                    Name = COALESCE(@name, Name),
                    IsSynced = 1,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @localId;
                """;
            updSess.Parameters.AddWithValue("@odooId", odooSessionId);
            updSess.Parameters.AddWithValue("@name", (object?)odooSessionName ?? DBNull.Value);
            updSess.Parameters.AddWithValue("@localId", localSessionId);
            await updSess.ExecuteNonQueryAsync();
        }

        await using (var updCfg = connection.CreateCommand())
        {
            updCfg.Transaction = transaction;
            updCfg.CommandText = """
                UPDATE PosConfigs
                SET CurrentSessionId = @odooId,
                    CurrentSessionName = COALESCE(@name, CurrentSessionName),
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE OdooId = @cfgId;
                """;
            updCfg.Parameters.AddWithValue("@odooId", odooSessionId);
            updCfg.Parameters.AddWithValue("@name", (object?)odooSessionName ?? DBNull.Value);
            updCfg.Parameters.AddWithValue("@cfgId", configOdooId);
            await updCfg.ExecuteNonQueryAsync();
        }

        await using (var updOrders = connection.CreateCommand())
        {
            updOrders.Transaction = transaction;
            updOrders.CommandText = """
                UPDATE PosOrders
                SET SessionOdooId = @odooId,
                    SessionName = COALESCE(@name, SessionName)
                WHERE SessionId = @localId;
                """;
            updOrders.Parameters.AddWithValue("@odooId", odooSessionId);
            updOrders.Parameters.AddWithValue("@name", (object?)odooSessionName ?? DBNull.Value);
            updOrders.Parameters.AddWithValue("@localId", localSessionId);
            await updOrders.ExecuteNonQueryAsync();
        }

        await using (var updPays = connection.CreateCommand())
        {
            updPays.Transaction = transaction;
            updPays.CommandText = """
                UPDATE PosPayments
                SET SessionOdooId = @odooId,
                    SessionName = COALESCE(@name, SessionName)
                WHERE SessionId = @localId;
                """;
            updPays.Parameters.AddWithValue("@odooId", odooSessionId);
            updPays.Parameters.AddWithValue("@name", (object?)odooSessionName ?? DBNull.Value);
            updPays.Parameters.AddWithValue("@localId", localSessionId);
            await updPays.ExecuteNonQueryAsync();
        }

        await using (var updQueue = connection.CreateCommand())
        {
            updQueue.Transaction = transaction;
            updQueue.CommandText = """
                UPDATE PendingSyncQueue
                SET SessionOdooId = @odooId
                WHERE Status != 'Completed'
                  AND LocalEntityId = @localId;
                """;
            updQueue.Parameters.AddWithValue("@odooId", odooSessionId);
            updQueue.Parameters.AddWithValue("@localId", localSessionId);
            await updQueue.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task BindLocalOrderToOdooOrderAsync(
        long localOrderId,
        string orderUuid,
        int odooOrderId,
        string? odooOrderName = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        await using (var updOrder = connection.CreateCommand())
        {
            updOrder.Transaction = transaction;
            updOrder.CommandText = """
                UPDATE PosOrders
                SET OdooId = @odooId,
                    Name = COALESCE(@odooName, Name),
                    IsSynced = 1,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @localId OR Uuid = @uuid;
                """;
            updOrder.Parameters.AddWithValue("@odooId", odooOrderId);
            updOrder.Parameters.AddWithValue("@odooName", (object?)odooOrderName ?? DBNull.Value);
            updOrder.Parameters.AddWithValue("@localId", localOrderId);
            updOrder.Parameters.AddWithValue("@uuid", orderUuid);
            await updOrder.ExecuteNonQueryAsync();
        }

        await using (var updLines = connection.CreateCommand())
        {
            updLines.Transaction = transaction;
            updLines.CommandText = """
                UPDATE PosOrderLines
                SET OrderOdooId = @odooId
                WHERE OrderId = @localId;
                """;
            updLines.Parameters.AddWithValue("@odooId", odooOrderId);
            updLines.Parameters.AddWithValue("@localId", localOrderId);
            await updLines.ExecuteNonQueryAsync();
        }

        await using (var updPays = connection.CreateCommand())
        {
            updPays.Transaction = transaction;
            updPays.CommandText = """
                UPDATE PosPayments
                SET OrderOdooId = @odooId,
                    IsSynced = 1
                WHERE OrderId = @localId;
                """;
            updPays.Parameters.AddWithValue("@odooId", odooOrderId);
            updPays.Parameters.AddWithValue("@localId", localOrderId);
            await updPays.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }
}