<div align="center">
<img src="./md_assets/logo.png" alt="cloudlog_helper_logo" width="25%" />

# Cloudlog Helper

![dotnet](https://img.shields.io/badge/.NET-6.0-512BD4?style=for-the-badge&logo=dotnet)
![avalonia](https://img.shields.io/badge/AvaloniaUI-11.2.6-0d6efd?style=for-the-badge)
![license](https://img.shields.io/badge/license-Unlicense-3451b2?style=for-the-badge&logo=none)<br />
![windows](https://img.shields.io/badge/Windows-7_SP1+-green?style=for-the-badge&logo=none)
![linux](https://img.shields.io/badge/Ubuntu-20.04+-green?style=for-the-badge&logo=none)<br />
![stage](https://img.shields.io/badge/Stage-UNDER_TESTING-orange?style=for-the-badge&logo=none)
![GitHub Release](https://img.shields.io/github/v/release/sydneyowl/cloudlog-helper?style=for-the-badge)

A lightweight companion utility for Cloudlog/Wavelog that automatically uploads current rig status and real-time QSO data. Supports most mainstream radios and seamless integration with JTDX/WSJT-X!

If your computer struggles with performance or you simply need an automated QSO/rig data upload solution, give Cloudlog Helper a try!

<img src="./md_assets/img.png" alt="interface_preview" width="60%" />

[🌍阅读中文版本](./readme_cn.md)
</div>

## 💻 Supported Platforms

+ Windows 7 SP1+
+ Ubuntu 20.04+ or other mainstream Linux distros
+ macOS adaptation in progress...

## ⚡️ Quick Start

> [!TIP]
> You may also compile from source - refer to the "Compilation" section below.
+ Download the appropriate build for your system from Releases. Linux users requiring rig data reporting should launch the app with sudo.

+ Launch the application and navigate to Settings → Basic Configuration.

### 📌 Cloudlog Setup

+ Enter your Cloudlog/Wavelog server URL and API KEY. Follow these steps to locate them:

<img src="./md_assets/image-20250510205625912.png" alt="api_configuration" width="80%" />

+ Click "Test". Upon successful validation, a station ID dropdown will appear. Select the correct station ID if you maintain multiple stations in Cloudlog/Wavelog - all subsequent QSOs will be logged under this ID.

![](./md_assets/image-20250510212652161.png)

### 📌 Hamlib Configuration

> [!NOTE]
> Skip this section if you don't require automatic rig data reporting.

> [!WARNING]
> When JTDX (or WSJT-X) is active, it acquires exclusive control of your radio. Refer to the "JTDX Integration" section for coexistence solutions.

This feature periodically uploads rig parameters (frequency, mode, etc.) to your Cloudlog server. During QSO logging, Cloudlog automatically populates these fields while displaying real-time rig data on its interface for operational reference.

+ Select your radio model from the dropdown
+ Specify the device port
+ Click "Test" before enabling "Automatic Rig Data Reporting". Save configurations with "Confirm".

<img src="./md_assets/hamlib.png" width="50%" />

+ The main interface should now display rig parameters, which should also appear on your Cloudlog dashboard:

<img src="./md_assets/image-20250511120812425.png" alt="cloudlog_dashboard" width="50%" />
+ Under "Station", select your radio to enable automatic field population during QSO logging.

<img src="./md_assets/image-20250510221120607.png" alt="qso_autofill" width="50%" />

### 📌 UDP Server Configuration

This GridTracker-like feature processes UDP broadcasts from JTDX containing decoded callsigns, frequencies, and signal reports, uploading QSO results to Cloudlog in real-time.

+ Default settings usually suffice. If modifying the port, ensure JTDX's UDP settings match. **For cross-device operation, enable "Allow External Connections" and set JTDX's UDP server IP to Cloudlog Helper's host machine.**

<img src="./md_assets/image-20250510222349765.png" alt="udp_settings" width="60%" />

+ The interface will display transmission status and completed QSOs:

<img src="./md_assets/image-20250510223010041.png" alt="qso_notification" width="30%" />

## 🚀 Advanced
### 🎯 JTDX Integration
To maintain rig data reporting while using JTDX:

When JTDX is active, it monopolizes radio control. However, both applications use Hamlib, and JTDX supports rigctld control. We'll create a rigctld instance for shared access:

1. In Cloudlog Helper's settings:
   - Configure radio parameters
   - Enable "Automatic Rig Data Reporting"
   - **Keep "Disable PTT Control" unchecked** (required by JTDX)

<img src="./md_assets/img1.png" alt="rig_settings" width="40%"/>

2. In JTDX:
   - Set "Radio" to "Hamlib NET rigctl"
   - Enter `127.0.0.1:4534` as CAT control server
   - Maintain default PTT method

<img src="./md_assets/img3.png" alt="jtdx_settings" width="40%" />

3. Verify CAT/PTT functionality before finalizing.

<img src="./md_assets/image-20250510140025232.png" alt="integration_success" width="70%" />

> [!WARNING]  
> Occasional "Connection forcibly closed" errors are normal due to request collisions. Increase the polling interval (10-30s) to mitigate. Future updates will optimize contention handling.

## 🛠️ Compilation
Prerequisites: `.NET 6.0+` and `gcc`. The following applies to Linux x64 - other platforms should reference `.github/workflows/build.yml`.

Clone the repository:
```shell
git clone --recursive --depth=1 https://github.com/SydneyOwl/cloudlog-helper.git
```
### 🔨 Hamlib Compilation
Skip if not needing rig data reporting.

We only require rigctld from Hamlib:
```shell
# Dependencies
sudo apt install build-essential gcc g++ cmake make libusb-dev libudev-dev

cd cloudlog-helper/hamlib
./bootstrap

# Optimized build configuration (referencing WSJT-X)
./configure --prefix=<INSTALL_DIR> --disable-shared --enable-static --without-cxx-binding \
CFLAGS="-g -O2 -fPIC -fdata-sections -ffunction-sections" \
LDFLAGS="-Wl,--gc-sections"

make -j4 all
make install-strip DESTDIR=""
```
Locate rigctld at ./<INSTALL_DIR>/bin.

### 🔨 Main Application
```shell
cd cloudlog-helper
dotnet restore -r linux-x64
dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true --self-contained true
```
The build outputs to bin/Release/net6.0/linux-64. Copy rigctld here if compiled.

## ✨ Miscellaneous
### 🐆 Performance Metrics
Tested on Windows 7 SP1 (x64) with i5-3337U/8GB RAM, running Rustdesk + JTDX + Cloudlog Helper + NetTime v3.14.

After 1-hour FT8 operation:

<img src="./md_assets/img_branchmark.png" width="30%" />

##  🎉 Many thanks to...
+ [WsjtxUtils](https://github.com/KC3PIB/WsjtxUtils), A class library and usage examples related to interacting with WSJT-X through the UDP interface in .NET and .NET Framework.

## 📝 License
Cloudlog Helper is a free and unencumbered software released into the public domain.
Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

Complete license terms available in the [Unlicense](./LICENSE) file.
