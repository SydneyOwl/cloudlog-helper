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

<img src="./md_assets/img_en.jpg" alt="interface_preview" width="60%" />

</div>

## 💻 Supported Platforms

+ Windows 7 SP1+
+ Ubuntu 20.04+ or other mainstream Linux distributions
+ macOS support is in progress...

## ⚡️ Quick Start!

> [!TIP]
> You can also choose to compile the software yourself. Refer to the `Compilation` section below.

+ Download the software for your system from the `Release` section. If you're using Linux and need the radio data reporting feature, launch the software with `sudo`.

+ Open the software, click `Settings` -> `Basic Settings` to access the configuration page.

### 📌 Cloudlog Configuration

+ Enter your Cloudlog / Wavelog server (hereafter referred to as Cloudlog) address and the corresponding API KEY. You can obtain the URL and API KEY by following the steps shown in the image below:

  <img src="./md_assets/image-20250510205625912.png" alt="image-20250510205625912" width="80%" />

+ Click "Test." If your input is correct, a dropdown for selecting the Station ID will appear below the API KEY. If you have multiple stations set up in Cloudlog / Wavelog, select the correct ID here. All subsequent QSOs will be uploaded to this ID.

  ![image-20250524114842178](./md_assets/image-20250524114842178.png)

### 📌 Clublog Configuration

+ Enter the callsign, email, and password you used when registering on Clublog.

![image-20250524114934649](./md_assets/image-20250524114934649.png)

+ Click "Test." If the test passes, you can enable "Automatically upload QSOs to Clublog" in the "UDP Settings."

  ![image-20250524115040135](./md_assets/image-20250524115040135.png)

### 📌 Hamlib Configuration

> [!NOTE]
> If you don’t need automatic radio data upload, you can skip this step.

> [!WARNING]
> When JTDX (or WSJT-X, hereafter referred to as JTDX) is running, it will monopolize control of the radio. Therefore, this feature and JTDX cannot be enabled simultaneously unless JTDX is properly configured. Refer to the `Working with JTDX` section for a solution.

This software can periodically upload radio information (frequency, mode, etc.) to your Cloudlog server. When logging a QSO, Cloudlog will automatically fetch the current frequency, mode, and other data to populate the corresponding fields, reducing manual input errors. Additionally, the Cloudlog interface will display real-time radio frequency and mode information for reference.

+ Select your radio model from the `Radio Model` dropdown.
+ Choose the port where your device is connected under `Device Port`.
+ Click the "Test" button. Only after a successful test should you check "Enable automatic radio data reporting." Click "Confirm" to save the settings.

<img src="./md_assets/image-20250524115143015.png" width="50%" />

+ The software’s main interface should now display the retrieved radio information. Open your Cloudlog website, and the homepage should show your radio details:

<img src="./md_assets/image-20250511120812425.png" alt="image-20250510221517526" width="50%" />

+ Under "Station," select your radio. From then on, when filling in QSO details, Cloudlog will automatically populate the frequency, mode, and other information.

<img src="./md_assets/image-20250510221120607.png" alt="image-20250510221120607" width="50%" />

### 📌 UDP Server Configuration

This feature works similarly to `GridTracker`. `JTDX` broadcasts decoded callsigns, frequencies, signal reports, etc., via the UDP protocol, and `CloudlogHelper` receives and decodes this information, uploading the QSO results in real time to your Cloudlog server.

+ Minimal configuration is required here. If you change the port number, ensure the UDP server settings in JTDX are updated accordingly. **Note: If JTDX and Cloudlog Helper are not running on the same machine, you must enable "Allow external connections" and set the UDP server IP address in JTDX to the IP of the machine running Cloudlog Helper.**

<img src="./md_assets/image-20250524115213082.png" alt="image-20250510222349765" width="60%" />

+ After this setup, the software’s main interface will display relevant information when JTDX is in transmit mode or after completing a QSO.

<img src="./md_assets/image-20250524115350845.png" alt="image-20250510223010041" width="30%" />

## 🚀 Advanced

### 🎯 Working with JTDX/WSJT-X

If you want to report radio data in real time while JTDX is running, follow these steps. The process for WSJT-X is similar.

When JTDX is running, it monopolizes control of the radio, preventing this software from reading the frequency. Fortunately, both JTDX and this software can use Rigctld as the radio control backend. Simply adjust JTDX’s network server settings to share a single Rigctld backend with this software.

> [!IMPORTANT]
> Do not set the polling intervals for JTDX and this software too short. Excessive requests may overwhelm the radio. A recommended value is 8s for JTDX (under Settings -> Radio) and 15s for this software. **Ensure the intervals are not integer multiples of each other.**

Here’s how to set it up (using Windows 7 as an example):

+ Open Cloudlog Helper, go to "Settings," enter your radio details, and enable "Automatic radio data reporting." **Do not** check `Disable PTT Control`, as JTDX relies on this feature for transmission.

+ Click "Apply Changes."

+ Open `JTDX`, go to `Settings` -> `Radio`, change `Radio Device` to `Hamlib NET rigctl`, and set the CAT control’s network server to the Rigctld backend address (default: `127.0.0.1:4534`). Leave the PTT method unchanged.

  <img src="./md_assets/image-20250519212931093.png" alt="image-20250517151541410" width="60%" />

+ Test CAT and PTT functionality, then click "OK."

+ You’ve now successfully enabled collaboration between CloudlogHelper and JTDX.

  <img src="./md_assets/image-20250510140025232.png" alt="image-20250510140025232" width="70%" />

### 🎯 Configuration Details

#### ⚙️ Hamlib Settings

| Setting                             | Description                                                  |
| ----------------------------------- | ------------------------------------------------------------ |
| Automatic radio data reporting      | If enabled, the software will upload radio data to the specified Cloudlog server. |
| Polling interval                    | Interval (in seconds) for querying radio data from the Rigctld backend. Default: 9s. |
| Radio model                         | The model of your radio. The list is fetched from Rigctld, so all Hamlib-supported radios are theoretically supported. |
| Device port                         | The port where your radio is connected.                      |
| Report split frequency              | Query split frequency (different TX/RX frequencies) from Rigctld. Some radios may not support this or return incorrect data. |
| Report TX power                     | Query current transmit power from Rigctld. Some radios may not support this or return incorrect data. |
| Advanced: Rigctld command-line args | Manually specify Rigctld startup arguments. This takes highest priority; if set, other settings (e.g., disable PTT/allow external control) are ignored. **If manually specifying args, you must explicitly set Rigctld’s IP and port (`-T <ip> -t <port>`).** |
| Advanced: Disable PTT control       | Disable RTS/DTR control at startup (adds `--set-conf=""rts_state=OFF"" --set-conf ""dtr_state=OFF""`). Only needed on some Linux systems. Do not enable if working with JTDX or other third-party software. |
| Advanced: Allow external control    | Allow non-localhost devices to interact with Rigctld (adds `-T 0.0.0.0`). |
| ~~Advanced: Enable request proxy~~  | ~~Start a proxy server to forward external requests to Rigctld.~~ (Deprecated) |
| Use external Rigctld service        | Use an external Rigctld instance as the backend (e.g., if you manually started one). |

#### ⚙️ UDP Server Settings

| Setting                      | Description                                                  |
| ---------------------------- | ------------------------------------------------------------ |
| Enable UDP server            | Start a UDP server to receive QSO data from third-party software. |
| Port number                  | UDP server port.                                             |
| Allow external connections   | Allow requests from non-localhost devices.                   |
| Auto-upload QSOs to Cloudlog | Automatically upload received QSOs to the specified Cloudlog server. |
| Auto-upload QSOs to Clublog  | Automatically upload received QSOs to the specified Clublog server. |
| QSO upload retry attempts    | Number of retries for failed QSO uploads.                    |

## 🛠️ Compilation

Ensure your environment has `.NET 6.0+` and `gcc`. The steps below are for Linux x64; other platforms can refer to `.github/workflows/build.yml`.

First, clone the repository:

```shell
git clone --recursive --depth=1 https://github.com/SydneyOwl/cloudlog-helper.git
```

### 🔨 Compiling Hamlib

Skip this step if you don’t need radio data reporting. The software can run without Hamlib.

We only need `rigctld`, a radio control daemon from the Hamlib toolkit that allows remote control via TCP:

```shell
# Install dependencies
sudo apt install build-essential gcc g++ cmake make libusb-dev libudev-dev

cd cloudlog-helper/hamlib
./bootstrap

# Optimize for size (similar to WSJT-X’s CMakeLists)
./configure --prefix=<INSTALL_DIR> --disable-shared --enable-static --without-cxx-binding \
CFLAGS="-g -O2 -fPIC -fdata-sections -ffunction-sections" \
LDFLAGS="-Wl,--gc-sections"

make -j4 all
make install-strip DESTDIR=""
```

After compilation, `rigctld` will be in `./<INSTALL_DIR>/bin`.

### 🔨 Compiling the Software

Run:

```shell
cd cloudlog-helper
dotnet restore -r linux-x64
dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true --self-contained true
```

The compiled software will be in `bin/Release/net6.0/linux-64`. Copy `rigctld` (if needed) here to complete the setup.

## ✨ Miscellaneous

### 🐆 Performance Analysis

A simulated FT8 remote operation scenario was tested on low-end hardware (Windows 7 SP1 x64, i5-3337U, 8GB RAM), running `Rustdesk` + `JTDX` + `Cloudlog Helper` + `NetTime v3.14`.

After 1 hour, CPU and memory usage were as follows (spikes correspond to decoding cycles):

<img src="./md_assets/img_branchmark.png" width="30%" />

## 🙏 Acknowledgments

+ [Hamlib](https://github.com/Hamlib/Hamlib): Amateur radio control library (supports radios, rotators, tuners, and amplifiers).
+ [WsjtxUtils](https://github.com/KC3PIB/WsjtxUtils): C# library and examples for interacting with WSJT-X via UDP in .NET/.NET Framework 4.8.

## 📝 License

Cloudlog Helper is a free and unencumbered software released into the public domain.
Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

Complete license terms available in the [Unlicense](./LICENSE) file.
