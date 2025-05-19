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

[**🌍阅读中文版本**](./readme_cn.md)

A lightweight companion utility for Cloudlog/Wavelog that automatically uploads current rig status and real-time QSO
data. Supports most mainstream radios and seamless integration with JTDX/WSJT-X!

If your computer struggles with performance or you simply need an automated QSO/rig data upload solution, give Cloudlog
Helper a try!

The application interface fully supports both **English** and **中文 (Chinese)**, While most screenshots in this
documentation currently display the Chinese interface, you can easily switch languages in settings!

<img src="./md_assets/img.png" alt="interface_preview" width="60%" />

**注意：此软件尚在开发中，暂时不建议您使用。如果您需要试用请自行编译。**

**Note: This software is currently under construction and is not recommended to use. You may compile the software yourself if you want to try it.**

</div>

## 💻 Supported Platforms

+ Windows 7 SP1+
+ Ubuntu 20.04+ and other mainstream Linux distributions
+ macOS support under development...

## ⚡ Quick Start

> [!TIP]
> You may also choose to compile from source - refer to the "Compilation" section below.

1. Download the appropriate version for your OS from Releases. **Linux users requiring radio data reporting should
   launch the software with `sudo`**.

2. Open the application and navigate to `Settings` → `Basic Settings`.

### 📌 Cloudlog Configuration

1. Enter your Cloudlog/Wavelog server URL and API KEY. Follow these steps to obtain them:

   <img src="./md_assets/image-20250510205625912.png" alt="API Key Retrieval" width="80%" />

2. Click "Test". Upon successful validation, a station ID dropdown will appear below the API key field. Select the
   correct ID if you maintain multiple stations in Cloudlog/Wavelog - all subsequent QSOs will be uploaded to this
   station.

   ![](./md_assets/image-20250510212652161.png)

### 📌 Hamlib Configuration

> [!NOTE]
> Skip this section if you don't require automatic radio data reporting.

> [!WARNING]
> Enabling JTDX/WSJT-X will grant it exclusive radio control. Refer to "Working with JTDX" for coexistence solutions.

This feature periodically uploads radio parameters (frequency, mode, etc.) to your Cloudlog server. When logging QSOs,
Cloudlog automatically populates these fields, eliminating manual entry errors. Your Cloudlog dashboard will also
display real-time radio data.

1. Select your radio model from the dropdown
2. Choose the correct COM port
3. Click "Test" before enabling "Automatic Radio Data Reporting"
4. Save configurations with "Confirm"

<img src="./md_assets/hamlib.png" width="50%" />

5. The main interface should now display radio data. Verify on your Cloudlog dashboard:

<img src="./md_assets/image-20250511120812425.png" width="50%" />

6. Select your radio under "Station" - Cloudlog will now auto-populate frequency/mode during QSO logging.

<img src="./md_assets/image-20250510221120607.png" width="50%" />

### 📌 UDP Server Configuration

This functions similarly to GridTracker. JTDX broadcasts decoded callsigns/frequency/reports via UDP, which
CloudlogHelper captures and uploads to your Cloudlog server.

1. Default settings usually suffice. If modifying the port, ensure JTDX's UDP settings match.

   **Important**: For cross-device operation, enable "Allow External Connections" and set JTDX's UDP IP to Cloudlog
   Helper's host IP.

<img src="./md_assets/image-20250510222349765.png" width="60%" />

2. The interface will display transmission status and completed QSOs:

<img src="./md_assets/image-20250510223010041.png" width="30%" />

## 🚀 Advanced Features

### 🎯 JTDX/WSJT-X Integration
If you wish to report radio data in real-time while using JTDX, please refer to the following instructions. The process for WSJT-X is similar.

When you start JTDX, the control of the radio will be exclusively held by JTDX, and you will no longer be able to read the radio frequency through this software. Fortunately, both JTDX and this software can use Rigctld as the backend for radio control. You only need to modify the network server address in JTDX so that this software and JTDX share the same Rigctld backend.

> [!IMPORTANT]
>
> Do not set the polling intervals for JTDX and this software too short. Excessive data requests may cause the radio to respond too slowly and result in errors. A recommended value is to set the interval to 8s in JTDX's Settings > Radio and the polling interval for this software to 15s. **Please note that the two intervals should not be integer multiples of each other.**

The specific steps are as follows (using Windows 7 as an example):

+ Open Cloudlog Helper, go to the "Settings" page, fill in the relevant radio information, and check "Automatic Radio Data Reporting." Note: **Do not** check `Disable PTT Control`. JTDX relies on this feature to control radio transmission.

+ Click "Apply Changes."

+ Open `JTDX`, go to `Settings` > `Radio`, change `Radio Device` to `Hamlib NET rigctl`, and enter the rigctld backend address in the CAT control network server field (default is 127.0.0.1:4534). Keep the PTT method configuration unchanged.

  <img src="./md_assets/image-20250519212931093.png" alt="image-20250517151541410" width="60%" />

+ After verifying that both CAT and PTT are functional, click "OK."

+ You have now successfully enabled collaboration between Cloudlog Helper and JTDX.

  <img src="./md_assets/image-20250510140025232.png" alt="image-20250510140025232" width="70%" />

### 🎯 Configuration Reference

#### ⚙️ Hamlib Settings

| Setting                                       | Description                                                 |
| --------------------------------------------- | ----------------------------------------------------------- |
| Automatic Radio Data Reporting                | Enables periodic uploads of radio parameters to Cloudlog    |
| Polling Interval                              | Frequency of rigctld queries (default: 9s)                  |
| Radio Model                                   | Supported models mirror Hamlib's compatibility              |
| COM Port                                      | Device communication port                                   |
| Report Split Frequency                        | Requests transceiver split frequency data (radio-dependent) |
| Report TX Power                               | Requests current transmission power (radio-dependent)       |
| Advanced - rigctld Arguments                  | Manual parameters (overrides other settings)                |
| Advanced - Disable PTT Control                | Disables RTS/DTR control (Linux-specific)                   |
| Advanced - Allow Remote Control               | Enables non-localhost rigctld access                        |
| 【deprecated】Advanced - Enable Request Proxy | Activates priority-based command mediation                  |
| Use External rigctld                          | Connect to existing rigctld instance                        |

#### ⚙️ UDP Server Settings

| Setting                  | Description                     |
|--------------------------|---------------------------------|
| Enable UDP Server        | Listens for QSO data broadcasts |
| Port                     | UDP listening port              |
| Allow Remote Connections | Accepts non-localhost packets   |
| Auto-upload QSOs         | Automatic Cloudlog submissions  |
| Upload Retry Attempts    | Failed transmission retries     |

## 🛠️ Compilation

Requires `.NET 6.0+` and `gcc`. (Linux x64 example - other OSes reference `.github/workflows/build.yml` (TBD))

```shell
git clone --recursive --depth=1 https://github.com/SydneyOwl/cloudlog-helper.git
```

### 🔨 Hamlib Compilation

Optional (skip if not using radio features). We only require `rigctld` - Hamlib's radio control daemon.

```shell
# Dependencies
sudo apt install build-essential gcc g++ cmake make libusb-dev libudev-dev

cd cloudlog-helper/hamlib
./bootstrap

# Optimized build (adapted from WSJT-X)
./configure --prefix=<INSTALL_DIR> --disable-shared --enable-static --without-cxx-binding \
CFLAGS="-g -O2 -fPIC -fdata-sections -ffunction-sections" \
LDFLAGS="-Wl,--gc-sections"

make -j4 all
make install-strip DESTDIR=""
```

Locate `rigctld` in `./<INSTALL_DIR>/bin`.

### 🔨 Application Compilation

```shell
cd cloudlog-helper
dotnet restore -r linux-x64
dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true --self-contained true
```

The compiled output resides in `bin/Release/net6.0/linux-64`. Place `rigctld` (if compiled) in this directory.

## ✨ Additional Information

### 🐆 Performance Metrics

Tested on Windows 7 SP1 (x64) with i5-3337U/8GB RAM, running:

- Rustdesk
- JTDX
- Cloudlog Helper
- NetTime v3.14

CPU/memory usage after 1-hour FT8 operation (spikes occur during decode cycles):

<img src="./md_assets/img_branchmark.png" width="30%" />

## 📝 License

Cloudlog Helper is a free and unencumbered software released into the public domain.
Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

Complete license terms available in the [Unlicense](./LICENSE) file.
