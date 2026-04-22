# Unity Agent Client

[![GitHub license](https://img.shields.io/github/license/yetsmarch/UnityAgentClient)](./LICENSE)
![Unity 2021.3+](https://img.shields.io/badge/unity-2021.3+-000.svg)

[English](README.md) | 日本語

Agent Client Protocol(ACP)を用いて、任意のAIエージェント(Gemini CLI, Claude Code, Codex CLI等)をUnityエディタに統合します。AIエージェントは60以上の組み込みMCPツールを通じて、シーン操作、コンポーネント編集、アセット管理などを直接実行できます。

![demo](/docs/images/img-demo.gif)

## 概要

Unity Agent Clientは、[Agent Client Protocol(ACP)](https://agentclientprotocol.com)を用いて、AIエージェントをUnityエディタに接続するエディタ拡張です。単なるチャットではなく、エージェントが60以上の組み込みツールを使ってプロジェクトを**操作**できます。

### 特徴

- **任意のAIエージェント** — ACP対応の全エージェントをサポート(Gemini CLI, Claude Code, Codex CLI, opencode, Goose等)
- **60以上の組み込みMCPツール** — メタツールにグループ化され、効率的なエージェント対話を実現
- **メタツールアーキテクチャ** — エージェントは60以上ではなく約12のツールを認識し、コンテキスト負荷を約80%削減
- **マルチセッション対応** — ツールバーのドロップダウンでセッション切替、`＋`ボタンで新規セッション作成
- **Elicitationフォーム** — エージェントがネイティブUIコントロール(トグル、スライダー、ドロップダウン、複数選択、カラーピッカー、Vector3、オブジェクトピッカー)で構造化入力を要求可能；URLモードはブラウザでOAuth認証を開始
- **スキルレシピ** — 一般的なタスクのための事前定義されたステップバイステップガイド(14の組み込みスキル)
- **拡張可能なツールシステム** — `IMcpTool`を実装するだけでカスタムツールを追加可能
- **デュアルMCPトランスポート** — エージェントの機能に基づきHTTP(Node.js不要)またはstdioプロキシを自動選択
- **自動再接続** — 指数バックオフによるエージェントクラッシュからの復旧、設定変更時の自動再接続

### What's Agent Client Protocol?

<img src="https://camo.githubusercontent.com/7de78d0f4d0f9755d0ed1aef979e0758dc64790f9c14831d0445d92dc6f36666/68747470733a2f2f7a65642e6465762f696d672f6163702f62616e6e65722d6461726b2e77656270">

[Agent Client Protocol](https://agentclientprotocol.com)はZedが提唱する、AIエージェントとコードエディタを繋ぐためのプロトコルです。JSON-RPCをベースとし、MCP(Model Context Protocol)との連携を念頭に設計されています。

ACPをサポートするAIエージェントの一覧: https://agentclientprotocol.com/overview/agents

### Why not Unity AI?

Unity 6.2以降では公式の[Unity AI](https://unity.com/products/ai)が利用可能ですが:

- **Unity AIはUnityのモデルに限定** — 自分のLLMプロバイダを選択できない
- **Unity Cloudトークンが必要** — クラウド接続と有料クレジットが必須
- **ツーリングが限定的** — 拡張可能なツールシステムがない

Unity Agent Clientは**モデル非依存**かつ**無料で利用可能**です。

## セットアップ

詳細なセットアップガイドは **[docs/SETUP.md](docs/SETUP.md)** を参照してください。

Unity Agent Clientを利用するにはUnity 2021.3以上が必要です。Node.jsはオプションです。

### 1. リポジトリのクローン

```bash
git clone https://github.com/3DLabInstruments/UnityACPClient.git
```

Unity Hubでクローンしたフォルダを開いてください。全てのDLLはバンドル済みで追加作業は不要です。

既存プロジェクトに追加する場合：`Assets/UnityAgentClient/`をプロジェクトの`Assets/`にコピーしてください（DLLは`Editor/Plugins/`に含まれています）。

### 2. 利用するAgentのセットアップ

`Project Settings > Unity Agent Client`を開き、利用したいAIエージェントに応じて設定を埋めます。

> [!NOTE]
> macOSの場合、zshでPATH解決がうまく行われないことがあります。`which`コマンドでバイナリのフルパスを確認し、Commandに入力してください。

> [!WARNING] 
> 設定はプロジェクトのUserSettingsフォルダに保存されます。APIキーなどを誤ってアップロードしないよう注意してください。

<details>
<summary>GitHub Copilot CLI</summary>

GitHub Copilotのサブスクリプションがあれば、追加のAPIキー不要で利用できます。

| Command   | Arguments |
| --------- | --------- |
| `copilot` | `--acp`   |
</details>

<details>
<summary>Gemini CLI</summary>

| Command  | Arguments            |
| -------- | -------------------- |
| `gemini` | `--experimental-acp` |

APIキーを利用する場合は、Environment Variablesに`GEMINI_API_KEY`を追加してください。
</details>

<details>
<summary>Claude Code</summary>

https://github.com/zed-industries/claude-code-acp

| Command           | Arguments |
| ----------------- | --------- |
| `claude-code-acp` | -         |
</details>

<details>
<summary>opencode (推奨)</summary>

https://opencode.ai/

| Command    | Arguments |
| ---------- | --------- |
| `opencode` | `acp`     |
</details>

<details>
<summary>Goose</summary>

https://block.github.io/goose/

| Command | Arguments |
| ------- | --------- |
| `goose` | `acp`     |
</details>

## 使い方

`Window > Unity Agent Client > AI Agent`からAIエージェントを開くと、自動でセッションへの接続が行われます。

![](/docs/images/img-agent-window.png)

- フィールドにプロンプトを入力し、**Enter**またはSendを押すことで送信。Shift+Enterで改行、入力欄は内容に応じて自動で拡張。実行中は**Esc**でキャンセル
- アセットをドラッグ&ドロップでコンテキストとしてアタッチ。添付はアイコン付きのチップで表示され、クリックでProjectウィンドウにピン、×ボタンで削除
- ツールバー左端の色付きドットが接続状態を示します（緑=接続中、黄=接続試行中、赤=失敗）。ホバーで詳細
- エージェント応答中は "thinking…" アニメーションを表示
- **マルチセッション：** ツールバーのドロップダウンで現在のエージェントの全セッションを表示。`＋`ボタンで新規セッション作成。セッションはDomain Reload後も自動復元（エージェント設定ごとに最大20件）。「切断」ボタンはありません — セッションは共存し、エージェントプロセスは稼働し続けます
- **Elicitation：** エージェントが構造化入力を必要とする場合（戦略の選択、ビルド設定など）、ネイティブUIフォームがインラインで表示されます — ドロップダウン、トグル、スライダー、複数選択チェックボックス、バリデーション付きテキスト入力。Submit、Decline、またはCancel（Esc）で応答
- 接続に失敗した場合は **Retry** と **Open Settings** ボタン、接続中は **Cancel** ボタンが表示されます
- ツール実行時にエージェントから許可を求められる場合あり
- ツールバーのモード名は人間が読みやすい形式（例: "Agent", "Plan"）で表示されます
- エージェント設定（コマンド/引数）を変更すると自動的に再接続 — ウィンドウを閉じる必要なし

## ベストプラクティス

Unity Agent Clientはエディタ上でのコーディングを**推奨していません。** C#スクリプト編集時のDomain Reloadによりエージェントプロセスが再起動されます。セッションリストと最終アクティブセッションは保持され、再接続後に自動復元されます。コーディングにはIDEやコードエディタのAIを利用してください。

Unity Agent Clientは以下の用途に最適です:

- **シーン構築** — 「ディレクショナルライトを追加して暖色に設定して」
- **プロジェクト分析** — 「Woodマテリアルを参照しているアセットは？」
- **設定確認** — 「Assets/UIのテクスチャのインポート設定を表示して」
- **プロトタイピング** — 「位置5,0,3にRigidbody付きのCubeをEnemyとして作成して」
- **デバッグ支援** — 「最近のコンソールエラーを表示して」

## ロードマップ

今後の計画は[ROADMAP.md](ROADMAP.md)を参照してください。

## ライセンス

このライブラリは[MIT LICENSE](LICENSE)の下で提供されています。