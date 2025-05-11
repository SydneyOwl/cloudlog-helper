<div align="center">
<img src="./md_assets/logo.png" alt="cloudlog_helper_logo" style="zoom:50%;" />

# Cloudlog Helper

![dotnet](https://img.shields.io/badge/.NET-6.0-512BD4?style=for-the-badge&logo=dotnet)
![avalonia](https://img.shields.io/badge/AvaloniaUI-11.2.6-0d6efd?style=for-the-badge)
![license](https://img.shields.io/badge/license-Unlicense-3451b2?style=for-the-badge&logo=none)<br />
![windows](https://img.shields.io/badge/Windows-7_SP1+-green?style=for-the-badge&logo=none)
![linux](https://img.shields.io/badge/Ubuntu-20.04+-green?style=for-the-badge&logo=none)<br />
![stage](https://img.shields.io/badge/Stage-UNDER_TESTING-orange?style=for-the-badge&logo=none)

[🌍点此阅读中文版本](./readme_cn.md)

A lightweight `Cloudlog`/`Wavelog` companion app that automatically uploads current radio status and real-time QSO data. Supports most mainstream radios and works seamlessly with `JTDX`/`WSJT-X` and similar software!

If your computer struggles with performance or you simply need an automated QSO/radio status upload tool, give `Cloudlog Helper` a try!

<img src="./md_assets/img.png" alt="img.png" style="zoom: 40%;" />
</div>

## 💻 Supported Platforms

+ Windows 7 SP1+
+ Ubuntu 20.04+ or other mainstream Linux distributions
+ macOS support coming soon...

## ⚡️ Quick Start!

> [!TIP]
> You can also compile from source – see the "Compilation" section below.
+ Download the appropriate version for your system from `Releases`.

+ Launch the app and navigate to `Settings` -> `Basic Settings`.

### 📌 Cloudlog Configuration

+ Enter your Cloudlog/Wavelog server URL (referred to as Cloudlog) and corresponding API KEY. Follow these steps to obtain the URL and API KEY:

  <img src="./md_assets/image-20250510205625912.png" alt="image-20250510205625912" style="zoom:67%;" />

+ Click "Test". If entered correctly, a dropdown for selecting Station ID will appear below the API key field. If you have multiple stations configured in Cloudlog/Wavelog, select the correct ID here – all subsequent QSOs will be uploaded to this ID.

  ![](./md_assets/image-20250510212652161.png)

### 📌 Hamlib Configuration

> [!NOTE]
> Skip this step if you don't need automatic radio data upload.

> [!WARNING]
> When JTDX (or WSJT-X, referred to as JTDX) is running, it will monopolize radio control. Without proper JTDX configuration, this feature cannot be used simultaneously with JTDX. See the "Working with JTDX" section for solutions.

This app can periodically upload radio status (frequency, mode, etc.) to your Cloudlog server. When logging QSOs, Cloudlog will automatically fetch and populate these fields, reducing manual entry errors. Additionally, Cloudlog's main interface will display real-time radio status for reference.

+ Select your radio model from the dropdown.
+ Choose the correct COM port for your device.
+ Click "Test". After successful testing, check "Enable automatic radio data upload" and click "Confirm" to save.

<img src="./md_assets/hamlib.png" style="zoom: 33%;" />

+ The main interface should now display radio information. Your Cloudlog homepage should show the radio status:

<img src="./md_assets/image-20250510221517526.png" alt="image-20250510221517526" style="zoom:33%;" /> <img src="./md_assets/image-20250510220742569.png" alt="image-20250510220742569" style="zoom:33%;" />

+ Under "Station", select your radio. Cloudlog will now auto-fill frequency/mode when logging QSOs.

<img src="./md_assets/image-20250510221120607.png" alt="image-20250510221120607" style="zoom:33%;" />

### 📌 UDP Server Configuration

This works similarly to `GridTracker`. JTDX broadcasts decoded callsigns, frequencies, and signal reports via UDP, which `CloudlogHelper` receives and uploads to your Cloudlog server.

+ Minimal configuration needed. If you change the port here, ensure JTDX's UDP settings match. **Note: If JTDX and Cloudlog Helper run on different machines, check "Allow external connections" and set JTDX's UDP server IP to Cloudlog Helper's host IP.**

<img src="./md_assets/image-20250510222349765.png" alt="image-20250510222349765" style="zoom:33%;" />

+ The main interface will now display JTDX transmission status or completed QSOs.

<img src="./md_assets/image-20250510223010041.png" alt="image-20250510223010041" style="zoom:60%;" />

## 🚀 Advanced
### 🎯 Working with JTDX
To enable radio data upload while JTDX is running:

When JTDX is active, it exclusively controls the radio, preventing this app from reading frequency data. However, both JTDX and Cloudlog Helper use Hamlib, and JTDX supports control via rigctld. We can run a rigctld instance for shared access:

(Windows 7 example):

+ In Cloudlog Helper's settings, configure your radio and enable "Automatic radio data upload". **Do not** check `Disable PTT control` – JTDX requires this for transmission.

  <img src="./md_assets/img1.png" alt="img.png" style="zoom: 40%;" />

+ Click "Apply Changes".

+ In JTDX, go to `Settings` -> `Radio`, change `Radio Device` to `Hamlib NET rigctl`, set CAT control server to `127.0.0.1:4534`, and keep PTT method unchanged:

  <img src="./md_assets/img3.png" alt="img.png" style="zoom: 50%;" />

+ Test CAT and PTT functionality, then confirm.

+ CloudlogHelper and JTDX are now working together.

  <img src="./md_assets/image-20250510140025232.png" alt="image-20250510140025232" style="zoom: 25%;" />

> [!WARNING]  
> Occasional "Connection forcibly closed" errors are normal due to polling conflicts between JTDX and this app. Increase this app's polling interval (10-30s) to reduce frequency. Future updates may improve this.

## 🛠️ Compilation
Ensure your environment has `.NET 6.0+` and `gcc`. (Linux instructions)

Clone the repository:
```shell
git clone --recursive --depth=1 https://github.com/SydneyOwl/cloudlog-helper.git
```

### 🔨 Compile Hamlib
Skip if you don't need radio data functionality.

We only need `rigctld` from Hamlib:
```shell
# Dependencies
sudo apt install build-essential gcc g++ cmake make libusb-dev libudev-dev

cd cloudlog-helper/hamlib
./bootstrap

# Optimized build (similar to WSJT-X)
./configure --prefix=<INSTALL_DIR> --disable-shared --enable-static --without-cxx-binding \
CFLAGS="-g -O2 -fPIC -fdata-sections -ffunction-sections" \
LDFLAGS="-Wl,--gc-sections"

make -j4 all
make install-strip DESTDIR=""
```
Find `rigctld` in `/<INSTALL_DIR>/bin`.

### 🔨 Compile the Application
```shell
cd cloudlog-helper
dotnet restore -r linux-x64
dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true --self-contained true
```
The compiled app will be in `bin/Release/net6.0/linux-64`. Copy `rigctld` here if compiled.

## ✨ Miscellaneous
### 🐆 Performance
Tested on low-end hardware (Windows 7 SP1 x64, i5-3337U, 8GB RAM) running `Rustdesk` + `JTDX` + `Cloudlog helper` + `NetTime v3.14`.

After 1 hour of FT8 operation:(CPU spikes occur during decoding cycles.)

<img src="./md_assets/img_branchmark.png" style="zoom: 67%;"  alt="Branchmark"/>

## 📝 License
`Cloudlog Helper` is free and unencumbered software released into the public domain. Anyone may use, modify, distribute, or sell this software for any purpose without restrictions.

Full license details in [Unlicense](./LICENSE).