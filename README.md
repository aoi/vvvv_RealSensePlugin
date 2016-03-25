# vvvv - Intel RealSense Plugin
vvvvでIntel RealSenseを使うためのプラグイン。

## ノード一覧

|ノード|内容|
|---|---|
|RGB|RGBカメラ映像の取得。|
|Depth|Depthカメラ映像/深度情報の取得。|
|Hand|手の検出。骨格、マスク。|
|Face|顔の検出。表情/表出情報。心拍数。|
|SpeechRecognition|音声認識。|
|3DScan (Unstable)|顔/物体などを3Dスキャンし3Dモデルを作成。|
|Segmentation (Unstable)|背景除去。|

## 動作環境
### 必須動作環境

|項目名|内容|
|----|----|
|PC|https://software.intel.com/en-us/intel-realsense-sdk の通り|
|RealSense Camera| F200, (R200 動作未確認) |
|vvvv|64bit版, DX11 Nodes|

### 開発時確認環境

|項目名|バージョン|
|---|---|
|OS|Windows 8.1 64bit|
|CPU|Intel Core i7-4702MQ|
|RealSense Camera|F200|
|RealSense SDK|2016 R1|
|RealSense Runtime|2016 R1|
|vvvv|45beta34.2 64bit|

## インストール
### Intel RealSense SDKのインストール
以下のサイトの手順通りにSDKをインストールする。
https://software.intel.com/en-us/intel-realsense-sdk/download

### vvvvのインストール
以下のサイトからvvvv 64bit Versionをダウンロード/インストールする。
https://vvvv.org/downloads

以下のサイトからDX11 Nodesをダウンロード/インストールする。
https://vvvv.org/contribution/directx11-nodes-alpha

### RealSenseプラグインのインストール


## 仕様
機能別対応解像度  
https://software.intel.com/sites/landingpage/realsense/camera-sdk/v1.1/documentation/html/index.html?doc_advanced_working_with_multiple_modaliti.html

## 動かない時は...

1. パッチを終了
1. タスクマネージャ>詳細に表示されている「RealSenseDCM.exe」のプロセスを終了
1. RealSenseカメラを差し直す
1. 他のUSBポートでも試してみる
1. PC再起動
1. 諦める
