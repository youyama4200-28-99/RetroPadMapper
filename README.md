# RetroPad Mapper

Nintendo Switch Online向けのファミコン／NES系Bluetoothコントローラーを、Windows上でキーボードやマウス入力に変換する常駐アプリです。SDL3が認識する一般的なゲームパッドでも利用できます。

> 非公式プロジェクトです。Nintendoおよび各製品名は各権利者の商標です。本プロジェクトは任天堂株式会社と提携・承認関係にありません。

## 主な機能

- Bluetooth接続したコントローラーを自動検出（NES / Famicom / Nintendo名を優先）
- 十字キー、A/B/X/Y、START/SELECT、L/R、HOMEを任意のキーへ割り当て
- 左・右・中クリック、ホイールへの割り当て
- タスクトレイ常駐、切断時の押しっぱなし防止
- SDL3のタイムスタンプ付きイベントを受けて即時出力（固定間隔ポーリングなし）
- 実機利用中のSDLイベント→処理開始／出力呼び出し時間を画面に表示
- HKCUを使った管理者権限不要の自動起動
- 設定を `%APPDATA%\RetroPadMapper\settings.json` に自動保存

## 使い方

1. Windowsの「Bluetoothとデバイス」でコントローラーをペアリングします。
2. `RetroPadMapper.exe` を起動します。
3. 画面で各ボタンの出力を選びます。
4. 必要なら「Windowsログイン時に自動起動」をオンにします。

ウィンドウを閉じても終了せず、タスクトレイに残ります。終了はトレイアイコンの右クリックメニューから行います。

### 制限事項

- Windows 10/11 x64向けです。
- 管理者として動くアプリへ入力を送るには、RetroPad Mapper側も同じ権限が必要な場合があります。
- これはキーボード／マウス変換です。仮想Xboxコントローラーは生成しません。保守終了したViGEmBusへの依存を避けるためです。
- Steam Inputなどが同じコントローラーを処理すると二重入力になる場合があります。その場合はSteam側の該当コントローラー設定を無効にしてください。

## ビルド

.NET 8 SDKが必要です。

```powershell
dotnet restore
dotnet build -c Release
dotnet publish src/RetroPadMapper/RetroPadMapper.csproj -c Release -r win-x64 --self-contained false -o artifacts/RetroPadMapper-win-x64
```

公開フォルダー一式を配布してください（SDL3.dllを含むため、exeだけを抜き出さないでください）。

## 入力遅延の検証

v0.1.0の入力処理は8msごとに状態を読む方式で、その周期だけで0〜8msの待ちが追加され得ました。v0.2.0はSDL3のタイムスタンプ付きゲームパッドイベントをスレッドセーフなイベントウォッチで直接処理し、接続検出だけを低頻度ループに分離しています。

再現可能なA/Bベンチマークは、同じ5,000件のタイムスタンプ付き入力列を旧8msポーリングとイベント通知へ同時に投入します。両方が全件取得し、イベント側のp95が旧方式より小さい場合だけ成功終了します。

```powershell
RetroPadMapper.exe --benchmark benchmarks/latest
```

このリポジトリで記録した結果と生データは [benchmarks/latest/RESULTS.md](benchmarks/latest/RESULTS.md) と [benchmarks/latest/latency-samples.csv](benchmarks/latest/latency-samples.csv) にあります。この試験が実証するのはアプリ内の起床・ディスパッチ遅延の差です。Bluetooth無線、コントローラーのファームウェア、SDL HIDバックエンド内部、ゲーム側の入力取得周期を含むエンドツーエンド遅延は主張しません。

## OSSとライセンス

本体は [MIT License](LICENSE) です。SDL3はzlib、C#バインディングとJetBrains AnnotationsはMITライセンスです。配布物に [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を同梱してください。Nintendoのロゴ、画像、ファームウェア、プロトコル由来コードは同梱していません。

脆弱性の報告方法は [SECURITY.md](SECURITY.md)、貢献方法は [CONTRIBUTING.md](CONTRIBUTING.md) を参照してください。
