# Odoo 20 Point of Sale (.NET Desktop Client)

A fast, offline-first **Odoo 20 Point of Sale (POS)** desktop application built with **C# / .NET 10 (WPF)**, **SQLite (WAL mode)**, and **Odoo JSON-RPC**.  
Designed for touchscreens and optimized to run smoothly with zero lag—even on low-spec POS terminals (**2 GB RAM + mechanical HDD**).

---

## ✨ Key Features

### 1. Offline-First Architecture & Fast Batch Odoo Sync
- **Local SQLite Database (`pos_data.db`)**: Runs in `WAL` (Write-Ahead Logging) mode with in-memory caching so all POS operations (adding items, switching categories, opening menus, validating payments) execute in **< 1 ms** with zero network blocking.
- **Sequential FIFO Offline Queue (`PendingSyncQueue`)**: Sessions (`OPEN_SESSION`), orders (`CREATE_ORDER`), cash moves (`CASH_IN_OUT`), and register closures (`CLOSE_SESSION`) are saved locally first and automatically flushed to Odoo in the background.
- **High-Speed Batch Upload & Local Tax Math**:
  - Calculates tax-excluded (`price_subtotal`) and tax-included (`price_subtotal_incl`) amounts locally in C# using Odoo's `HALF-UP` rounding rules—eliminating per-line `account.tax.compute_all` RPC calls.
  - Uploads queued orders and payments in batched JSON-RPC calls.
  - Automatically syncs Opening Balance (`cash_register_balance_start`) and Closing Balance (`cash_register_balance_end_real`) to Odoo sessions.

### 2. Per-User Odoo Credentials & Role-Based Access (`POS-Only` Mode)
- **Per-User Odoo Server Credentials**: Each user can have their own Odoo `Server URL`, `Database`, `Username / Login`, and `Password / API Key`. Every session opened, order created, and register closed is authenticated and logged in Odoo's chatter under the exact cashier who performed it.
- **Touch PIN Pad Authentication**: Cashiers log in by tapping their user card and entering their POS security PIN.
- **User Roles & Permissions**:
  - **Administrator**: Full access to the navigation sidebar, POS Dashboard, Catalog, Sessions, Orders, Payments, and Settings.
  - **POS-Only Mode**: Bypasses the dashboard and sidebar completely—taking the cashier directly to the **Opening Control** screen of their assigned POS register, with a **Logout** button in the 3-dot POS menu and on the Opening Control screen.

### 3. Customizable POS Register & Payment Layouts
- **Two POS Register Layouts** (selectable in *PoS Settings*):
  1. **Standard Odoo Layout**: Cart & Numpad on the left, Categories & Products on the right.
  2. **Split 4-Quadrant Layout**: Selected Order Items & Total in the **top-left**, Numpad & Payment buttons in the **top-right**, Vertical Category Column in the **bottom-left**, and Products Grid in the **bottom-right**.
- **Two Payment Screen Layouts** (selectable in *PoS Settings*):
  1. **Standard Split Payment Layout**: Payment methods & numpad on the left, giant total & payment summary on the right.
  2. **Centered Vertical Payment Layout**: Giant total, active tender pill, payment methods, change due, quick-tender bar (`+0.50`, `+1`, `+2`, `+5`), and numpad all centered vertically.
- **Advanced POS Workflow Options**:
  - **Numbers First Mode**: Enter the quantity on the numpad before tapping a product.
  - **One-Click Payment**: Instant payment button right next to the main Payment button.
  - **Prevent Line Merging**: Always create separate order lines instead of merging quantities.
  - **Product Variants Configurator**: Interactive attribute & extra-price selection popup.
  - **Hide Opening Cash Control / Hide Closing Register / Hide Review Orders**.
  - **Built-in Zoom Control**: Chrome-style zoom (`60%` to `180%`) inside the 3-dot POS menu.

### 4. Multi-Language POS Interface (English & Arabic)
- Switch the POS interface between **English** and **Arabic (`العربية`)** with the **Cairo** font from **Settings → Language**.
- Translates the **POS-Only / Opening Control Screen**, **POS Register**, **POS Orders** (including localized dates, `AM/PM` as `ص/م`, `Paid` / `مدفوع`, `Print Receipt` / `طباعة الإيصال`, and light-text `Cash` & `Change` summaries), **Payment View** (including payment method names), **Change View**, and **3-Dot POS Menu**.

### 5. Thermal Receipt Printing & Custom XML Designs
- **RAW ESC/POS Hardware Printing (`winspool.Drv`)**: Supports `80mm` and `58mm` thermal printers, automatic receipt printing, cash drawer kick-out pulse (`ESC p`), and multiple character encodings (`UTF-8`, `CP864`, `Windows-1256`, `CP437`).
- **Custom Receipt Designs**: Built-in XML templates for **Standard Order Receipt**, **Pastry Receipt**, **Cafe Receipt**, **Simplified Kitchen / Second Receipt**, **Open Cash Drawer Pulse Only**, and **Sales Details Report (`report_saledetails.xml`)**.
- **Last Session Sales Details Printing**: Print the Sales Details report directly from the Opening Control screen when the register is closed, or automatically when closing a session.

### 6. Automated Database Backup & Restore
- **Scheduled Automatic Backups**: Configure a target backup folder and schedule automatic hot backups (**Daily** or **Weekly** at a specific `HH:mm` 24-hour time).
- **One-Click Manual Backup & Restore**: Create an instant `.db` backup with **Backup Now**, or select any previous `.db` backup file to **Import & Restore Database** atomically without restarting the app.

---

## 🔄 Synchronized Odoo Models (12 Models)

| # | Odoo Model | Local SQLite Table | Description |
|---|------------|--------------------|-------------|
| 1 | `res.company` | `Companies` | Company details, logos, Tax ID, ICE, currency, and branches |
| 2 | `res.users` | `Users` | Users, avatars, roles (`Administrator` / `POS Only`), PINs, and Odoo credentials |
| 3 | `pos.payment.method` | `PosPaymentMethods` | Cash, Card/Bank, Customer Account, and Online payment methods |
| 4 | `pos.category` | `PosCategories` | PoS product categories, Odoo 20 pastel color palette, and shop links |
| 5 | `product.attribute` / `.value` | `ProductAttributes` / `Values` | Product attributes, display types, color swatches, and extra prices |
| 6 | `product.tag` | `ProductTags` | Product tags and color badges |
| 7 | `product.combo` / `.item` | `ComboChoices` / `Lines` | Combo choices and included product lines |
| 8 | `product.template` | `Products` | Products, variants count, prices, taxes (`price_include`), barcodes, and SKUs |
| 9 | `pos.config` | `PosConfigs` | POS shops, session states, 7-day sales sparklines, and shop settings |
| 10 | `pos.session` | `PosSessions` | POS register sessions, opening/closing balances, and state pipeline |
| 11 | `pos.order` / `.line` | `PosOrders` / `PosOrderLines` | POS orders, order lines, taxes, tendered cash, change, and refund links |
| 12 | `pos.payment` | `PosPayments` | POS payment transactions |

---

## 🛠️ Tech Stack & Requirements

- **Framework**: `.NET 10.0` (`net10.0-windows`, WPF)
- **MVVM Toolkit**: `CommunityToolkit.Mvvm` `8.4.2`
- **Database**: `Microsoft.Data.Sqlite` `10.0.14` (SQLite with `WAL` mode)
- **Image Processing**: `SixLabors.ImageSharp` `3.1.12` (WebP, PNG, JPEG, and SVG avatar/product thumbnail decoding with 128px memory caching)
- **Minimum Target Hardware**: Windows 10 / 11 (x64 or x86), 2 GB RAM, HDD or SSD

---

## 🚀 Getting Started (Development)

### 1. Clone & Run
```powershell
git clone https://github.com/your-org/Pos-Odoo-.Net.git
cd Pos-Odoo-.Net
dotnet restore
dotnet run
```

### 2. Initial Odoo Setup & Sync
1. Launch the application and click **⚙ Odoo Setup & Sync** in the top-right corner of the login screen.
2. Go to **Settings → Odoo Connection & Sync**.
3. Enter your **Server URL** (e.g. `http://localhost:8069`), **Database Name**, **Username**, and **Password / API Key**, then click **Save Credentials** and **Test Connection**.
4. Click **Sync All Data from Odoo** to pull all 12 Odoo modules into the local SQLite database.
5. Go to **Users**, select a user, configure their **Per-User Odoo Credentials**, **Role** (`Administrator` or `POS Only`), **Assigned POS Register**, and **POS Security PIN**, then click **Save**.

---

## 📦 Building for Production & Creating the Windows Installer

### 1. Publish a Native `ReadyToRun` Build
Run the following command in PowerShell from the project root:

```powershell
# For 64-bit Windows POS Terminals
dotnet publish -c Release -r win-x64 --self-contained true -o .\publish\win-x64

# For 32-bit Windows POS Terminals
dotnet publish -c Release -r win-x86 --self-contained true -o .\publish\win-x86
```

### 2. Compile the Installer (`Setup.exe`)
1. Open **`setup.iss`** in **[Inno Setup Compiler](https://jrsoftware.org/isdl.php)**.
2. Click **Build → Compile** (`Ctrl + F9`).
3. The standalone installer will be generated in **`Installer\OdooPOS_Setup_v1.0.0.exe`**.

---

## 📂 Project Structure

```text
├── Assets/                  # Payment method icons & static resources
├── Converters/              # WPF IValueConverter implementations
├── Models/                  # Data models (Orders, Sessions, Products, Users, Configs, Settings)
├── Services/
│   ├── OdooSyncService.cs           # JSON-RPC client, batch order uploader & FIFO queue processor
│   ├── PosPrinterService.cs         # Win32 RAW ESC/POS thermal printer & cash drawer service
│   ├── ReceiptTemplateService.cs    # Receipt & Sales Details report formatter
│   └── SqliteDatabaseService.cs     # SQLite WAL repository, migrations, cache & backup/restore
├── Utils/
│   └── PosImageUtils.cs             # Base64/WebP/SVG image decoder, thumbnail cache & app icon
├── ViewModels/              # MVVM ViewModels for all POS & Backend screens
├── Views/                   # WPF XAML Views & Windows
├── App.xaml                 # Global Odoo 20 design system, Cairo font & Chrome scrollbar styles
├── Pos.csproj               # .NET 10 project configuration
└── setup.iss                # Inno Setup Windows installer script
```