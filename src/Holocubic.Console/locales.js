(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.HoloLocale=api;})(globalThis,function(){
 const codes=['zh-CN','zh-TW','en','ja','de'];
 // Each row is source Chinese | Traditional Chinese | English | Japanese | German.
 const rows=`
服务数据|服務資料|Service data|サービスデータ|Dienstdaten
查看完整传感器，并选择发送到设备的读数。|查看完整感測器，並選擇傳送至裝置的讀數。|View all sensors and choose the readings sent to the device.|すべてのセンサーを表示し、デバイスに送信する値を選択します。|Alle Sensoren anzeigen und die an das Gerät gesendeten Werte auswählen.
采集来源|採集來源|Data sources|データ取得元|Datenquellen
全部传感器|全部感測器|All sensors|すべてのセンサー|Alle Sensoren
搜索传感器|搜尋感測器|Search sensors|センサーを検索|Sensoren suchen
硬件、传感器名称或来源|硬體、感測器名稱或來源|Hardware, sensor name or source|ハードウェア、センサー名、取得元|Hardware, Sensorname oder Quelle
硬件分类|硬體分類|Hardware category|ハードウェア分類|Hardwarekategorie
全部硬件|全部硬體|All hardware|すべてのハードウェア|Gesamte Hardware
没有匹配的传感器|沒有符合的感測器|No matching sensors|一致するセンサーがありません|Keine passenden Sensoren
读数为 0 时正常显示；暂时无数据的传感器显示 --。|讀數為 0 時正常顯示；暫無資料的感測器顯示 --。|Zero readings are shown as 0. Sensors without a current reading show --.|値が 0 の場合は 0、現在のデータがない場合は -- と表示します。|Nullwerte werden als 0 angezeigt. Ohne aktuellen Messwert erscheint --.
设备字段映射|裝置欄位對應|Device field mapping|デバイス項目の割り当て|Gerätefeld-Zuordnung
自动选择适合的读数，也可指定显卡、硬盘或风扇。只列出单位相同的传感器。|自動選擇適合的讀數，也可指定顯示卡、硬碟或風扇。僅列出單位相同的感測器。|Select readings automatically or choose a specific GPU, disk or fan. Only sensors with matching units are listed.|自動選択、または特定の GPU・ディスク・ファンを指定できます。同じ単位のセンサーのみ表示します。|Werte automatisch oder von einer bestimmten GPU, Festplatte oder einem Lüfter wählen. Nur Sensoren mit passender Einheit werden angeboten.
全部设为自动|全部設為自動|Set all to automatic|すべて自動に設定|Alle auf automatisch setzen
保存设备映射|儲存裝置對應|Save device mapping|デバイスの割り当てを保存|Gerätezuordnung speichern
映射已保存|對應已儲存|Mapping saved|割り当てを保存済み|Zuordnung gespeichert
映射已修改，点击保存后生效|對應已修改，按儲存後生效|Mapping changed. Save to apply.|割り当てを変更しました。保存すると適用されます。|Zuordnung geändert. Zum Anwenden speichern.
正在保存映射…|正在儲存對應…|Saving mapping…|割り当てを保存中…|Zuordnung wird gespeichert…
设备传感器映射已保存|裝置感測器對應已儲存|Device sensor mapping saved|デバイスのセンサー割り当てを保存しました|Gerätesensor-Zuordnung gespeichert
自动选择（推荐）|自動選擇（建議）|Automatic (recommended)|自動選択（推奨）|Automatisch (empfohlen)
自动选择|自動選擇|Automatic|自動選択|Automatisch
已指定传感器|已指定感測器|Sensor assigned|センサー指定済み|Sensor zugewiesen
传感器已断开|感測器已中斷|Sensor disconnected|センサー未接続|Sensor getrennt
单位不匹配|單位不符|Unit mismatch|単位が一致しません|Einheit stimmt nicht überein
当前映射不可用|目前對應無法使用|Current mapping unavailable|現在の割り当ては利用不可|Aktuelle Zuordnung nicht verfügbar
待保存|待儲存|Unsaved|未保存|Nicht gespeichert
暂无采集来源|暫無採集來源|No data sources available|データ取得元がありません|Keine Datenquellen verfügbar
未启用|未啟用|Disabled|無効|Deaktiviert
可用|可用|Available|利用可能|Verfügbar
更新时间|更新時間|Updated|更新時刻|Aktualisiert
自动汇总|自動彙總|Automatic summary|自動集計|Automatische Übersicht
显卡|顯示卡|Graphics cards|グラフィックスカード|Grafikkarten
内存|記憶體|Memory|メモリ|Arbeitsspeicher
主板|主機板|Motherboard|マザーボード|Mainboard
硬盘|硬碟|Storage|ストレージ|Datenträger
风扇与控制器|風扇與控制器|Fans and controllers|ファンとコントローラー|Lüfter und Steuerungen
系统|系統|System|システム|System
其他|其他|Other|その他|Sonstige
CPU 使用率|CPU 使用率|CPU usage|CPU 使用率|CPU-Auslastung
CPU 温度|CPU 溫度|CPU temperature|CPU 温度|CPU-Temperatur
CPU 最高温度|CPU 最高溫度|CPU maximum temperature|CPU 最高温度|Maximale CPU-Temperatur
CPU 核心平均温度|CPU 核心平均溫度|Average CPU core temperature|CPU コア平均温度|Mittlere CPU-Kerntemperatur
CPU 频率|CPU 頻率|CPU clock|CPU クロック|CPU-Takt
CPU 电压|CPU 電壓|CPU voltage|CPU 電圧|CPU-Spannung
CPU 封装功耗|CPU 封裝功耗|CPU package power|CPU パッケージ電力|CPU-Paketleistung
CPU 核心功耗|CPU 核心功耗|CPU cores power|CPU コア電力|CPU-Kernleistung
CPU 总线频率|CPU 匯流排頻率|CPU bus clock|CPU バスクロック|CPU-Bustakt
CPU 风扇|CPU 風扇|CPU fan|CPU ファン|CPU-Lüfter
GPU 使用率|GPU 使用率|GPU usage|GPU 使用率|GPU-Auslastung
GPU 温度|GPU 溫度|GPU temperature|GPU 温度|GPU-Temperatur
GPU 热点温度|GPU 熱點溫度|GPU hotspot temperature|GPU ホットスポット温度|GPU-Hotspot-Temperatur
GPU 显存温度|GPU 顯示記憶體溫度|GPU memory temperature|GPU メモリ温度|GPU-Speichertemperatur
GPU 频率|GPU 頻率|GPU clock|GPU クロック|GPU-Takt
GPU 显存频率|GPU 顯示記憶體頻率|GPU memory clock|GPU メモリクロック|GPU-Speichertakt
GPU 功耗|GPU 功耗|GPU power|GPU 電力|GPU-Leistung
GPU 电压|GPU 電壓|GPU voltage|GPU 電圧|GPU-Spannung
已用显存|已用顯示記憶體|Used VRAM|使用中の VRAM|Belegter Grafikspeicher
显存总量|顯示記憶體總量|Total VRAM|VRAM 容量|Gesamter Grafikspeicher
GPU 风扇|GPU 風扇|GPU fan|GPU ファン|GPU-Lüfter
内存使用率|記憶體使用率|Memory usage|メモリ使用率|Speicherauslastung
已用内存|已用記憶體|Used memory|使用中のメモリ|Belegter Arbeitsspeicher
内存总量|記憶體總量|Total memory|メモリ容量|Gesamter Arbeitsspeicher
网络下载|網路下載|Network download|ネットワーク受信|Netzwerk-Download
网络上传|網路上傳|Network upload|ネットワーク送信|Netzwerk-Upload
磁盘读取|磁碟讀取|Disk read|ディスク読み取り|Datenträger lesen
磁盘写入|磁碟寫入|Disk write|ディスク書き込み|Datenträger schreiben
磁盘温度|磁碟溫度|Disk temperature|ディスク温度|Datenträgertemperatur
磁盘健康度|磁碟健康度|Disk health|ディスクの健全性|Datenträgerzustand
主板温度|主機板溫度|Motherboard temperature|マザーボード温度|Mainboard-Temperatur
VRM 温度|VRM 溫度|VRM temperature|VRM 温度|VRM-Temperatur
机箱风扇|機殼風扇|Case fan|ケースファン|Gehäuselüfter
水泵转速|水泵轉速|Pump speed|ポンプ回転数|Pumpendrehzahl
ChatGPT 账号与用量|ChatGPT 帳號與用量|ChatGPT account and usage|ChatGPT アカウントと使用量|ChatGPT-Konto und Nutzung
请连接与当前客户端相同的账号。登录后每分钟更新额度和使用统计，设备同步当前模型对应的额度。|請連接與目前用戶端相同的帳號。登入後每分鐘更新額度與用量，裝置同步目前模型的額度。|Connect the same account as this client. Quotas and usage refresh every minute; the device receives the current model's quota.|現在のクライアントと同じアカウントで接続してください。使用量は毎分更新され、現在のモデルの割当をデバイスに同期します。|Dasselbe Konto wie in diesem Client verbinden. Nutzung wird jede Minute aktualisiert; das Gerät erhält das Kontingent des aktuellen Modells.
登录信息仅保存在控制台专属目录。设备只接收用量数据，不接收账号或登录凭据。|登入資訊僅存於控制台專屬目錄。裝置僅接收用量，不接收帳號或憑證。|Login is stored in the console's own directory. Only usage data is sent to the device, never account details or credentials.|ログイン情報はコンソール専用フォルダーに保存します。デバイスには使用量のみ送信します。|Die Anmeldung liegt im eigenen Konsolenordner. Nur Nutzungsdaten werden an das Gerät gesendet, keine Kontodaten oder Zugangsdaten.
等待浏览器登录|等待瀏覽器登入|Waiting for browser login|ブラウザーでのログイン待ち|Warten auf Browser-Anmeldung
账号已连接|帳號已連接|Account connected|アカウント接続済み|Konto verbunden
尚未连接 ChatGPT 账号|尚未連接 ChatGPT 帳號|No ChatGPT account connected|ChatGPT アカウント未接続|Kein ChatGPT-Konto verbunden
请在官方登录页面完成登录，完成后此处自动更新。|請在官方登入頁面完成登入，完成後此處自動更新。|Complete sign-in on the official page. This panel updates automatically.|公式ページでログインすると、この画面が自動更新されます。|Auf der offiziellen Seite anmelden. Diese Ansicht aktualisiert sich automatisch.
设备登录码|裝置登入碼|Device code|デバイスコード|Gerätecode
重新打开登录页|重新開啟登入頁|Reopen sign-in page|ログインページを再表示|Anmeldeseite erneut öffnen
取消登录|取消登入|Cancel sign-in|ログインをキャンセル|Anmeldung abbrechen
立即刷新用量|立即重新整理用量|Refresh usage now|使用量を今すぐ更新|Nutzung jetzt aktualisieren
断开账号连接|中斷帳號連接|Disconnect account|アカウント接続を解除|Konto trennen
连接 ChatGPT 账号|連接 ChatGPT 帳號|Connect ChatGPT account|ChatGPT アカウントに接続|ChatGPT-Konto verbinden
使用设备码登录|使用裝置碼登入|Sign in with device code|デバイスコードでログイン|Mit Gerätecode anmelden
5 小时|5 小時|5 hours|5 時間|5 Stunden
每周|每週|Weekly|毎週|Wöchentlich
每天|每天|Daily|毎日|Täglich
额度周期|額度週期|Quota window|割当期間|Kontingentzeitraum
已用|已用|Used|使用済み|Verbraucht
剩余|剩餘|Remaining|残り|Verbleibend
重置时间|重設時間|Reset time|リセット日時|Zurücksetzung
额度更新时间|額度更新時間|Quota updated|割当の更新日時|Kontingent aktualisiert
统计更新时间|統計更新時間|Statistics updated|統計の更新日時|Statistik aktualisiert
Token 使用统计|Token 使用統計|Token usage statistics|Token 使用統計|Token-Nutzungsstatistik
累计 Token|累計 Token|Lifetime tokens|累計 Token|Tokens insgesamt
单日最高 Token|單日最高 Token|Peak daily tokens|1 日の最大 Token|Höchste Tagesnutzung
连续使用天数|連續使用天數|Current streak (days)|連続使用日数|Aktuelle Serie (Tage)
最长连续天数|最長連續天數|Longest streak (days)|最長連続日数|Längste Serie (Tage)
最长任务（秒）|最長任務（秒）|Longest task (seconds)|最長タスク（秒）|Längste Aufgabe (Sekunden)
最近每日用量|最近每日用量|Recent daily usage|最近の日別使用量|Letzte tägliche Nutzung
未返回的统计显示为 --。额度重置时间不是订阅到期日，接口暂不提供订阅到期日。|未回傳的統計顯示為 --。額度重設時間並非訂閱到期日，介面未提供訂閱到期日。|Unavailable statistics show --. Quota resets are separate from subscription expiry, which the API does not provide.|未取得の統計は -- と表示します。割当リセット日時は契約期限ではありません。契約期限は API で取得できません。|Nicht verfügbare Werte erscheinen als --. Die Zurücksetzung ist nicht das Abonnementende; dieses wird nicht bereitgestellt.
额度暂不可用，将自动重试|額度暫時無法取得，將自動重試|Quota unavailable; retrying automatically|割当を取得できません。自動再試行します|Kontingent nicht verfügbar; erneuter Versuch folgt
使用统计暂不可用|使用統計暫時無法取得|Usage statistics unavailable|使用統計を取得できません|Nutzungsstatistik nicht verfügbar
账号接口初始化失败|帳號介面初始化失敗|Account service initialization failed|アカウントサービスの初期化失敗|Kontodienst konnte nicht initialisiert werden
无法读取登录状态|無法讀取登入狀態|Could not read sign-in status|ログイン状態を取得できません|Anmeldestatus konnte nicht gelesen werden
无法开始登录，请重试或使用设备码登录|無法開始登入，請重試或使用裝置碼|Could not start sign-in. Retry or use a device code.|ログインを開始できません。再試行するかデバイスコードを使用してください|Anmeldung fehlgeschlagen. Erneut versuchen oder Gerätecode verwenden.
取消登录失败|取消登入失敗|Could not cancel sign-in|ログインをキャンセルできません|Anmeldung konnte nicht abgebrochen werden
断开连接失败|中斷連接失敗|Could not disconnect|接続を解除できません|Verbindung konnte nicht getrennt werden
账号接口连接中断，请重试|帳號介面連接中斷，請重試|Account connection interrupted. Please retry.|アカウント接続が切断されました。再試行してください|Kontoverbindung unterbrochen. Bitte erneut versuchen.
登录已中断，请重试|登入已中斷，請重試|Sign-in interrupted. Please retry.|ログインが中断されました。再試行してください|Anmeldung unterbrochen. Bitte erneut versuchen.
登录未完成，请重试|登入未完成，請重試|Sign-in incomplete. Please retry.|ログイン未完了です。再試行してください|Anmeldung nicht abgeschlossen. Bitte erneut versuchen.
未找到 Codex 运行组件，请安装 ChatGPT / Codex 客户端|找不到 Codex 執行元件，請安裝 ChatGPT / Codex 用戶端|Codex runtime not found. Install the ChatGPT / Codex client.|Codex ランタイムが見つかりません。ChatGPT / Codex をインストールしてください|Codex-Laufzeit fehlt. ChatGPT / Codex installieren.
登录已超时，请重试|登入已逾時，請重試|Sign-in timed out. Please retry.|ログインがタイムアウトしました。再試行してください|Anmeldung abgelaufen. Bitte erneut versuchen.
无法打开浏览器，请点击重新打开登录页|無法開啟瀏覽器，請重新開啟登入頁|Could not open browser. Reopen the sign-in page.|ブラウザーを開けません。ログインページを再表示してください|Browser konnte nicht geöffnet werden. Anmeldeseite erneut öffnen.
自动同步本地任务日志|自動同步本機任務日誌|Sync local task events automatically|ローカルタスクのイベントを自動同期|Lokale Aufgaben automatisch synchronisieren
直接提取当前客户端的任务事件，无需重启 ChatGPT。只同步状态相关字段，不保存或转发聊天正文。|直接擷取目前用戶端的任務事件，無需重新啟動 ChatGPT。僅同步狀態欄位，不儲存或轉送聊天內容。|Reads task events from this client without restarting ChatGPT. Chat text is not stored or forwarded.|ChatGPT を再起動せずにタスクのイベントを取得します。チャット本文は保存・転送しません。|Liest Aufgabenereignisse ohne ChatGPT-Neustart. Chattexte werden nicht gespeichert oder weitergeleitet.
可选 Hook 接入|可選 Hook 接入|Optional hook integration|任意の Hook 接続|Optionale Hook-Anbindung
配置 Hook|設定 Hook|Configure hooks|Hook を設定|Hooks konfigurieren
Hook 配置写入后仍需客户端加载并审查，写入成功不代表已收到事件。|Hook 設定寫入後仍需用戶端載入並審查，寫入成功不代表已收到事件。|Saved hooks still need client loading and review. Saving does not confirm event delivery.|保存した Hook はクライアントで読み込み・確認が必要です。保存だけでは受信を確認できません。|Gespeicherte Hooks müssen geladen und geprüft werden. Speichern bestätigt keinen Ereignisempfang.
Hook 配置已写入，触发状态以实际事件为准|Hook 設定已寫入，觸發狀態以實際事件為準|Hooks saved; real events confirm activation|Hook 保存済み。実際のイベントで動作を確認|Hooks gespeichert; echte Ereignisse bestätigen die Aktivierung
本地任务同步已暂停|本機任務同步已暫停|Local task sync paused|ローカル同期は一時停止中|Lokale Synchronisierung pausiert
未找到本地任务日志|找不到本機任務日誌|Local task logs not found|ローカルタスクログが見つかりません|Keine lokalen Aufgabenprotokolle gefunden
已收到本地任务事件|已收到本機任務事件|Local task events received|ローカルタスクイベントを受信済み|Lokale Aufgabenereignisse empfangen
正在同步本地任务事件|正在同步本機任務事件|Syncing local task events|ローカルイベントを同期中|Lokale Aufgabenereignisse werden synchronisiert
正在监听，等待新任务|正在監聽，等待新任務|Listening for new tasks|新しいタスクを待機中|Warten auf neue Aufgaben
模型|模型|Model|モデル|Modell
上下文占用|上下文使用量|Context usage|コンテキスト使用量|Kontextbelegung
5 小时额度已用|5 小時額度已用|5-hour quota used|5 時間枠の使用量|5-Stunden-Kontingent verbraucht
5 小时额度重置|5 小時額度重設|5-hour quota resets|5 時間枠のリセット|Zurücksetzung des 5-Stunden-Kontingents
每周额度已用|每週額度已用|Weekly quota used|週間枠の使用量|Wochenkontingent verbraucht
每周额度重置|每週額度重設|Weekly quota resets|週間枠のリセット|Zurücksetzung des Wochenkontingents
本地日志没有当前模型对应的额度数据。|本機日誌沒有目前模型對應的額度資料。|Local logs have no quota data for this model.|このモデルの割当データはローカルログにありません。|Lokale Protokolle enthalten keine Kontingentdaten für dieses Modell.
ChatGPT 与常见 AI 的工作状态同步|ChatGPT 與常見 AI 的工作狀態同步|Work status from ChatGPT and other AI apps|ChatGPT や各種 AI の作業状態を同期|Arbeitsstatus von ChatGPT und anderen KI-Apps
同步 AI 工作状态到设备。|同步 AI 工作狀態到裝置。|Sync AI activity to your device.|AI の作業状態をデバイスに同期します。|KI-Aktivität mit dem Gerät synchronisieren.
AI 状态同步|AI 狀態同步|AI activity sync|AI 状態の同期|KI-Status synchronisieren
发送测试状态|傳送測試狀態|Send test status|テスト状態を送信|Teststatus senden
测试仅验证推送链路，真实接入请查看事件来源和时间。|測試僅驗證推送連線，實際接入請查看事件來源和時間。|The test checks delivery only. Check the source and time for real activity.|テストは送信経路のみ確認します。実際の状態は送信元と時刻で確認してください。|Der Test prüft nur die Übertragung. Quelle und Zeit zeigen echte Aktivität.
本地工作任务|本機工作任務|Local work tasks|ローカル作業タスク|Lokale Arbeitsaufgaben
支持当前 ChatGPT / Codex 客户端的本地任务。首次接入后重启客户端，并按提示审查 Hook。|支援目前 ChatGPT / Codex 用戶端的本機任務。首次接入後重新啟動用戶端，並依提示審查 Hook。|Supports local tasks in ChatGPT / Codex. Restart the client after setup and review the hooks when prompted.|ChatGPT / Codex のローカルタスクに対応します。設定後にクライアントを再起動し、案内に従って Hook を確認してください。|Unterstützt lokale ChatGPT-/Codex-Aufgaben. Client nach der Einrichtung neu starten und Hooks auf Aufforderung prüfen.
重新配置|重新設定|Configure again|再設定|Erneut einrichten
接入本地工作任务|接入本機工作任務|Connect local tasks|ローカルタスクを接続|Lokale Aufgaben verbinden
常见 AI 网页|常見 AI 網頁|AI websites|AI ウェブサイト|KI-Websites
生成请求识别为主，页面按钮识别为补充。扩展提供手动同步与暂停开关。|主要識別生成請求，頁面按鈕識別為輔。擴充功能提供手動同步與暫停開關。|Detects generation requests with button detection as a fallback. Manual sync and pause are available.|生成リクエストを主に検出し、ボタン検出で補完します。手動同期と一時停止も可能です。|Erkennt Generierungsanfragen, ergänzt durch Schaltflächenerkennung. Manuelle Synchronisierung und Pause sind verfügbar.
打开浏览器扩展文件夹|開啟瀏覽器擴充功能資料夾|Open extension folder|拡張機能フォルダーを開く|Erweiterungsordner öffnen
打开 Edge 或 Chrome 的扩展管理，开启开发者模式。|開啟 Edge 或 Chrome 的擴充功能管理，啟用開發人員模式。|Open Edge or Chrome extensions and enable developer mode.|Edge または Chrome の拡張機能でデベロッパーモードを有効にします。|Erweiterungen in Edge oder Chrome öffnen und Entwicklermodus aktivieren.
选择加载解压缩的扩展程序，选择打开的文件夹。|選擇載入解壓縮的擴充功能，選取開啟的資料夾。|Choose Load unpacked and select the opened folder.|「パッケージ化されていない拡張機能を読み込む」で開いたフォルダーを選びます。|Entpackte Erweiterung laden und den geöffneten Ordner auswählen.
刷新 AI 网页，保持 Holopet 服务运行。|重新整理 AI 網頁，保持 Holopet 服務運作。|Refresh AI pages and keep Holopet running.|AI ページを再読み込みし、Holopet を起動したままにします。|KI-Seiten neu laden und Holopet weiter ausführen.
原生客户端中的普通聊天需使用对应网页版或手动同步。|原生用戶端中的一般聊天需使用對應網頁版或手動同步。|For regular chats in native apps, use the web version or manual sync.|ネイティブアプリの通常チャットはウェブ版または手動同期を使用してください。|Für normale Chats in nativen Apps die Webversion oder manuelle Synchronisierung verwenden.
只同步状态和来源，不发送问题、回复或工具内容。|僅同步狀態和來源，不傳送問題、回覆或工具內容。|Only status and source are sent, without prompts, replies or tool contents.|状態と送信元のみ同期し、質問・回答・ツール内容は送信しません。|Nur Status und Quelle werden gesendet, keine Fragen, Antworten oder Tool-Inhalte.
打开使用说明|開啟使用說明|Open instructions|使用方法を開く|Anleitung öffnen
Holopet 服务运行中|Holopet 服務運作中|Holopet is running|Holopet は実行中|Holopet läuft
Holopet 服务未启动|Holopet 服務未啟動|Holopet is stopped|Holopet は停止中|Holopet ist gestoppt
事件来源|事件來源|Event source|イベントの送信元|Ereignisquelle
最近状态|最近狀態|Latest status|最新の状態|Letzter Status
接收时间|接收時間|Event time|イベント時刻|Ereigniszeit
已写入客户端配置|已寫入用戶端設定|Client configuration saved|クライアント設定を保存済み|Client-Konfiguration gespeichert
尚未配置|尚未設定|Not configured|未設定|Nicht konfiguriert
等待真实事件|等待實際事件|Waiting for live activity|実際のイベントを待機中|Warten auf echte Aktivität
未识别|未識別|Unrecognized|未検出|Nicht erkannt
思考中|思考中|Thinking|思考中|Denkt nach
执行中|執行中|Working|実行中|In Bearbeitung
协作中|協作中|Collaborating|共同作業中|Zusammenarbeit
等待确认|等待確認|Waiting for approval|確認待ち|Warten auf Bestätigung
出错|出錯|Error|エラー|Fehler
空闲|閒置|Idle|待機中|Leerlauf
休眠|休眠|Sleeping|スリープ|Ruhezustand
已配置，请重启客户端并审查 Hook|已設定，請重新啟動用戶端並審查 Hook|Configured. Restart the client and review hooks.|設定済みです。クライアントを再起動して Hook を確認してください。|Konfiguriert. Client neu starten und Hooks prüfen.
测试状态已发送|測試狀態已傳送|Test status sent|テスト状態を送信しました|Teststatus gesendet
holocubic控制台|holocubic控制台|holocubic Console|holocubic コンソール|holocubic Konsole
设备控制台|裝置控制台|Device console|デバイスコンソール|Gerätekonsole
最小化|最小化|Minimize|最小化|Minimieren
最大化|最大化|Maximize|最大化|Maximieren
关闭|關閉|Close|閉じる|Schließen
扫描局域网设备|掃描區域網路裝置|Scan LAN|LAN を検索|LAN durchsuchen
连接设备|連接裝置|Connect device|デバイスに接続|Gerät verbinden
添加设备|新增裝置|Add device|デバイスを追加|Gerät hinzufügen
电脑服务|電腦服務|PC services|PC サービス|PC-Dienste
高级工具|進階工具|Advanced tools|詳細ツール|Erweiterte Tools
软件设置|軟體設定|App settings|アプリ設定|App-Einstellungen
PC App 语言|PC App 語言|App language|アプリの言語|App-Sprache
界面语言|介面語言|Interface language|表示言語|Anzeigesprache
立即应用，并保存在本机。|立即套用，並儲存在本機。|Applied immediately and saved on this PC.|すぐに適用し、この PC に保存します。|Wird sofort angewendet und auf diesem PC gespeichert.
设备控制|裝置控制|Device control|デバイス操作|Gerätesteuerung
文件管理|檔案管理|File manager|ファイル管理|Dateiverwaltung
媒体资源|媒體資源|Media|メディア|Medien
应用商店|應用程式商店|App store|アプリストア|App-Store
网络|網路|Network|ネットワーク|Netzwerk
后退|上一頁|Back|戻る|Zurück
前进|下一頁|Forward|進む|Vorwärts
刷新|重新整理|Refresh|更新|Aktualisieren
设备控制面板|裝置控制面板|Device dashboard|デバイスダッシュボード|Geräteübersicht
完整设备页面|完整裝置頁面|Full device page|デバイスの全機能|Vollständige Geräteseite
返回启动器|返回啟動器|Back to launcher|ランチャーに戻る|Zum Launcher
当前应用|目前應用程式|Current app|現在のアプリ|Aktuelle App
进入应用页面|進入應用程式頁面|Open controls|操作画面を開く|Steuerung öffnen
退出应用|結束應用程式|Exit app|アプリを終了|App beenden
运行中|執行中|Running|実行中|Aktiv
未启动|未啟動|Stopped|停止中|Gestoppt
已停止|已停止|Stopped|停止中|Gestoppt
停止|停止|Stop|停止|Stoppen
启动|啟動|Start|起動|Starten
设置|設定|Settings|設定|Einstellungen
打开|開啟|Open|開く|Öffnen
应用页面|應用程式頁面|App controls|アプリ操作|App-Steuerung
启动器|啟動器|Launcher|ランチャー|Launcher
个应用|個應用程式|apps|アプリ|Apps
个服务|個服務|services|サービス|Dienste
设备应用|裝置應用程式|Device apps|デバイスのアプリ|Geräte-Apps
设备服务|裝置服務|Device services|デバイスサービス|Gerätedienste
安装、更新与管理设备应用|安裝、更新與管理裝置應用程式|Install, update and manage device apps|アプリのインストール・更新・管理|Geräte-Apps installieren, aktualisieren und verwalten
管理 ›|管理 ›|Manage ›|管理 ›|Verwalten ›
连接状态|連線狀態|Connection|接続状態|Verbindung
设备热点|裝置熱點|Device hotspot|デバイスのアクセスポイント|Geräte-Hotspot
已连接|已連線|Connected|接続済み|Verbunden
在线|上線|Online|オンライン|Online
离线|離線|Offline|オフライン|Offline
IP 地址|IP 位址|IP address|IP アドレス|IP-Adresse
常用设置|常用設定|Quick settings|基本設定|Grundeinstellungen
设置直接保存到当前设备|設定直接儲存至目前裝置|Settings are saved directly to this device|設定はこのデバイスに保存されます|Einstellungen werden direkt auf diesem Gerät gespeichert
设备设置读取失败，请刷新后再保存|讀取裝置設定失敗，請重新整理後再儲存|Could not read settings. Refresh before saving.|設定を読み込めません。更新してから保存してください。|Einstellungen konnten nicht gelesen werden. Vor dem Speichern aktualisieren.
时区|時區|Time zone|タイムゾーン|Zeitzone
天气地址|天氣地址|Weather location|天気の地域|Wetterort
开机自启动|開機自動啟動|Startup app|起動時のアプリ|Autostart-App
关闭自启动|關閉自動啟動|Disable autostart|自動起動しない|Autostart deaktivieren
屏幕亮度|螢幕亮度|Brightness|画面の明るさ|Helligkeit
无线热点|無線熱點|Wi-Fi hotspot|Wi-Fi アクセスポイント|WLAN-Hotspot
保持开启|保持開啟|Keep enabled|常に有効|Aktiv lassen
有网络时关闭|有網路時關閉|Disable when connected|接続時は無効|Bei Verbindung deaktivieren
设备语言|裝置語言|Device language|デバイスの言語|Gerätesprache
简体中文|簡體中文|Simplified Chinese|簡体字中国語|Chinesisch (vereinfacht)
保存设置|儲存設定|Save settings|設定を保存|Einstellungen speichern
恢复默认|恢復預設|Restore defaults|初期値に戻す|Standardwerte
指示灯设置|指示燈設定|LED settings|LED 設定|LED-Einstellungen
唤醒屏幕|喚醒螢幕|Wake screen|画面を起動|Bildschirm aktivieren
设备固件更新 ›|裝置韌體更新 ›|Firmware update ›|ファームウェア更新 ›|Firmware aktualisieren ›
正在连接设备|正在連接裝置|Connecting to device|デバイスに接続中|Verbindung wird hergestellt
暂时无法连接设备|暫時無法連接裝置|Device unavailable|デバイスに接続できません|Gerät nicht erreichbar
正在读取真实设备状态…|正在讀取實際裝置狀態…|Reading device status…|デバイスの状態を取得中…|Gerätestatus wird gelesen…
请确认设备已开机，并与电脑处于同一局域网。|請確認裝置已開機，並與電腦位於同一區域網路。|Make sure the device is on and connected to the same LAN.|デバイスの電源と同じ LAN への接続を確認してください。|Gerät einschalten und mit demselben LAN verbinden.
重新连接|重新連線|Reconnect|再接続|Erneut verbinden
设备页面|裝置頁面|Device page|デバイスページ|Geräteseite
正在打开…|正在開啟…|Opening…|開いています…|Wird geöffnet…
返回控制面板|返回控制面板|Back to dashboard|ダッシュボードに戻る|Zur Übersicht
正在连接设备页面…|正在連接裝置頁面…|Connecting to device page…|デバイスページに接続中…|Geräteseite wird geladen…
网络配置|網路設定|Network settings|ネットワーク設定|Netzwerkeinstellungen
设备没有已安装服务|裝置沒有已安裝的服務|No services installed|サービスがありません|Keine Dienste installiert
设备列表与控制台更新。|裝置清單與控制台更新。|Devices, language and app updates.|デバイス、言語、アプリの更新。|Geräte, Sprache und App-Updates.
设备 Web 控制|裝置 Web 控制|Device web control|デバイスの Web 操作|Web-Gerätesteuerung
软件更新|軟體更新|App updates|アプリの更新|App-Updates
更新源地址|更新來源位址|Update feed URL|更新フィード URL|Update-Feed-URL
也可以直接导入发布者提供的 ZIP 更新包。|也可以直接匯入發布者提供的 ZIP 更新套件。|You can also import a ZIP update from the publisher.|配布元の ZIP 更新パッケージも読み込めます。|Alternativ ein ZIP-Update des Herausgebers importieren.
保存地址|儲存位址|Save URL|URL を保存|URL speichern
检查更新|檢查更新|Check for updates|更新を確認|Nach Updates suchen
导入本地更新包|匯入本機更新套件|Import update package|更新パッケージを読み込む|Update-Paket importieren
设备管理|裝置管理|Manage devices|デバイス管理|Geräte verwalten
重命名|重新命名|Rename|名前を変更|Umbenennen
设备重命名|裝置重新命名|Rename device|デバイス名を変更|Gerät umbenennen
设备名称|裝置名稱|Device name|デバイス名|Gerätename
名称只保存在此电脑，用于识别设备。|名稱僅儲存在此電腦，用於識別裝置。|This display name is saved on this PC.|この PC に表示名を保存します。|Der Anzeigename wird auf diesem PC gespeichert.
设备名称已保存|裝置名稱已儲存|Device name saved|デバイス名を保存しました|Gerätename gespeichert
设备名称不能为空且不能超过 50 个字符|裝置名稱不可為空，且不得超過 50 個字元|Enter a name of 1–50 characters|1～50 文字の名前を入力してください|Einen Namen mit 1–50 Zeichen eingeben
设备信息保存在本机，可修改设备的显示名称。|裝置資訊儲存在本機，可修改裝置的顯示名稱。|Device display names are stored on this PC and can be renamed.|デバイスの表示名はこの PC に保存され、変更できます。|Gerätenamen werden auf diesem PC gespeichert und können geändert werden.
管理本机为设备提供的服务。|管理本機提供給裝置的服務。|Manage the PC services used by your device.|デバイス用の PC サービスを管理します。|PC-Dienste für das Gerät verwalten.
正在读取服务状态…|正在讀取服務狀態…|Reading service status…|サービスの状態を取得中…|Dienststatus wird gelesen…
相关设备应用打开后，自动启动并配置对应服务。|相關裝置應用程式開啟後，自動啟動並設定對應服務。|Start and configure services when the matching device app opens.|対応するアプリを開くとサービスを自動起動・設定します。|Dienste beim Öffnen der passenden Geräte-App automatisch starten und konfigurieren.
打开应用时自动配置电脑服务|開啟應用程式時自動設定電腦服務|Configure PC services automatically|PC サービスを自動設定|PC-Dienste automatisch konfigurieren
服务页面|服務頁面|Service page|サービスページ|Dienstseite
随控制台启动|隨控制台啟動|Start with console|コンソールと同時に起動|Mit der Konsole starten
手动停止后会保持停止状态，重新打开相关设备应用时恢复自动配置。|手動停止後會保持停止，重新開啟相關裝置應用程式時恢復自動設定。|A manually stopped service stays stopped until its device app is reopened.|手動で停止したサービスは、対応するアプリを再度開くまで停止します。|Manuell gestoppte Dienste bleiben bis zum erneuten Öffnen der Geräte-App gestoppt.
服务由其他程序启动|服務由其他程式啟動|Started by another program|別のプログラムで起動済み|Von einem anderen Programm gestartet
配置会保存在本机。|設定會儲存在本機。|Settings are saved on this PC.|設定はこの PC に保存されます。|Einstellungen werden auf diesem PC gespeichert.
正在读取…|正在讀取…|Loading…|読み込み中…|Wird geladen…
重新自动配置当前设备|重新自動設定目前裝置|Reconfigure current device|現在のデバイスを再設定|Aktuelles Gerät neu konfigurieren
桌面投屏设置|桌面投影設定|Desktop mirror settings|画面転送の設定|Bildschirmübertragung
支持已连接的显示器、虚拟显示器和指定区域。|支援已連接的螢幕、虛擬螢幕和指定區域。|Choose a connected display, virtual display or screen region.|接続済み画面、仮想画面、指定範囲を選べます。|Monitor, virtuellen Bildschirm oder Bereich auswählen.
显示器|顯示器|Display|ディスプレイ|Monitor
主显示器|主顯示器|Primary display|メイン画面|Hauptmonitor
画面适配|畫面適配|Image fit|表示方法|Bildanpassung
铺满屏幕|填滿螢幕|Stretch|引き伸ばす|Strecken
完整显示|完整顯示|Contain|全体を表示|Einpassen
居中裁剪|置中裁切|Crop|中央で切り抜く|Zuschneiden
帧率|影格率|Frame rate|フレームレート|Bildrate
JPEG 质量|JPEG 品質|JPEG quality|JPEG 品質|JPEG-Qualität
指定区域（留空使用整个显示器）|指定區域（留空使用整個螢幕）|Region (empty for the full display)|範囲（空欄で画面全体）|Bereich (leer für gesamten Monitor)
x,y,宽,高|x,y,寬,高|x,y,width,height|x,y,幅,高さ|x,y,Breite,Höhe
保存投屏设置|儲存投影設定|Save mirror settings|画面転送設定を保存|Übertragungseinstellungen speichern
电脑监控设置|電腦監控設定|PC monitor settings|PC モニター設定|PC-Monitor-Einstellungen
保留旧 App 的硬件采集与 SSE 数据协议。|保留舊 App 的硬體擷取與 SSE 資料協定。|Hardware monitoring with the existing SSE protocol.|既存の SSE プロトコルでハードウェアを監視します。|Hardwareüberwachung mit dem bisherigen SSE-Protokoll.
暂不可用|暫時無法使用|Unavailable|利用不可|Nicht verfügbar
部分温度与功耗需要管理员传感器权限。|部分溫度與功耗需要系統管理員感測器權限。|Some temperature and power sensors need administrator access.|一部の温度・電力センサーには管理者権限が必要です。|Einige Temperatur- und Leistungssensoren benötigen Administratorrechte.
启动管理员传感器|啟動系統管理員感測器|Start elevated sensors|管理者センサーを起動|Sensoren als Administrator starten
保留原服务协议。|保留原服務協定。|Uses the existing service protocol.|既存のサービスプロトコルを使用します。|Verwendet das bisherige Dienstprotokoll.
串口、日志与设备开发入口。|序列埠、日誌與裝置開發入口。|Serial tools, logs and device development.|シリアル、ログ、デバイス開発ツール。|Serielle Werkzeuge, Protokolle und Geräteentwicklung.
串口配网|序列埠配網|USB Wi-Fi setup|USB Wi-Fi 設定|WLAN über USB einrichten
扫描热点、连接 Wi-Fi|掃描熱點、連接 Wi-Fi|Scan networks and connect to Wi-Fi|ネットワーク検索と Wi-Fi 接続|Netzwerke suchen und WLAN verbinden
串口终端|序列埠終端機|Serial terminal|シリアル端末|Serielles Terminal
连接与收发命令|連線與收發命令|Connect, send and receive|接続とコマンド送受信|Verbinden, senden und empfangen
运行日志|執行日誌|App logs|アプリログ|App-Protokolle
查看与导出软件日志|檢視與匯出軟體日誌|View and export app logs|ログの表示と書き出し|App-Protokolle anzeigen und exportieren
下载记录|下載記錄|Downloads|ダウンロード履歴|Downloads
本次运行中的下载任务|本次執行期間的下載工作|Downloads in this session|今回のダウンロード|Downloads dieser Sitzung
设备开发工具|裝置開發工具|Device developer tools|デバイス開発ツール|Geräte-Entwicklertools
SD 卡文件与 Lua 编辑|SD 卡檔案與 Lua 編輯|SD files and Lua editor|SD ファイルと Lua エディター|SD-Dateien und Lua-Editor
应用安装和媒体转换进度显示在对应设备页面中。|應用程式安裝與媒體轉換進度顯示於對應裝置頁面。|App installation and media conversion progress appear on their device pages.|アプリのインストールとメディア変換の進行状況はデバイスページに表示されます。|App-Installation und Medienkonvertierung zeigen ihren Fortschritt auf den Geräteseiten.
通过 USB 为设备连接 Wi-Fi。|透過 USB 為裝置連接 Wi-Fi。|Connect the device to Wi-Fi over USB.|USB 経由で Wi-Fi を設定します。|Das Gerät über USB mit WLAN verbinden.
连接 USB 数据线，并在设备上打开 WiFi Setting Guide。|連接 USB 傳輸線，並在裝置開啟 WiFi Setting Guide。|Connect a USB cable and open WiFi Setting Guide on the device.|USB ケーブルを接続し、デバイスで WiFi Setting Guide を開いてください。|USB-Kabel verbinden und WiFi Setting Guide auf dem Gerät öffnen.
正在读取串口…|正在讀取序列埠…|Reading serial ports…|シリアルポートを取得中…|Serielle Anschlüsse werden gelesen…
设备串口|裝置序列埠|Device serial port|デバイスのシリアルポート|Serieller Geräteanschluss
串口|序列埠|Serial port|シリアルポート|Serieller Anschluss
波特率|鮑率|Baud rate|ボーレート|Baudrate
没有检测到串口|未偵測到序列埠|No serial ports found|シリアルポートがありません|Keine seriellen Anschlüsse gefunden
打开设备配网应用|開啟裝置配網應用程式|Open Wi-Fi setup on device|Wi-Fi 設定アプリを開く|WLAN-Einrichtung auf dem Gerät öffnen
扫描 Wi-Fi|掃描 Wi-Fi|Scan Wi-Fi|Wi-Fi を検索|WLAN suchen
取消等待|取消等待|Cancel waiting|待機を中止|Warten abbrechen
Wi-Fi 名称（SSID）|Wi-Fi 名稱（SSID）|Wi-Fi name (SSID)|Wi-Fi 名（SSID）|WLAN-Name (SSID)
选择上方热点或手动输入|選擇上方熱點或手動輸入|Select a network or enter its name|ネットワークを選択または入力|Netzwerk auswählen oder Namen eingeben
Wi-Fi 密码|Wi-Fi 密碼|Wi-Fi password|Wi-Fi パスワード|WLAN-Passwort
开放网络可留空|開放網路可留空|Leave empty for an open network|公開ネットワークは空欄|Bei offenen Netzwerken leer lassen
显示密码|顯示密碼|Show password|パスワードを表示|Passwort anzeigen
连接 Wi-Fi|連接 Wi-Fi|Connect Wi-Fi|Wi-Fi に接続|WLAN verbinden
串口连接自动复用。设备成功联网后，读取到的 IP 会自动加入设备列表。|序列埠連線自動重用。裝置成功連線後，IP 會自動加入裝置清單。|The serial connection is reused. Connected devices are added to the device list automatically.|シリアル接続を再利用し、接続したデバイスを一覧に追加します。|Die serielle Verbindung wird wiederverwendet. Verbundene Geräte werden automatisch hinzugefügt.
连接 USB 串口，查看设备输出并发送命令。|連接 USB 序列埠，檢視裝置輸出並傳送命令。|Connect a USB serial port to view output and send commands.|USB シリアルで出力の表示とコマンド送信を行います。|USB-Anschluss verbinden, Ausgaben anzeigen und Befehle senden.
默认 115200 · 8N1；连接时保持 DTR / RTS 关闭。|預設 115200 · 8N1；連線時保持 DTR / RTS 關閉。|Default: 115200 · 8N1; DTR / RTS remain off.|初期値：115200 · 8N1、DTR / RTS は無効。|Standard: 115200 · 8N1; DTR / RTS bleiben aus.
断开|中斷連線|Disconnect|切断|Trennen
连接|連線|Connect|接続|Verbinden
未连接设备|尚未連接裝置|No device connected|未接続|Kein Gerät verbunden
等待串口数据…|等待序列埠資料…|Waiting for serial data…|シリアルデータを待機中…|Warte auf serielle Daten…
输入发送内容|輸入傳送內容|Enter text to send|送信内容を入力|Zu sendenden Text eingeben
换行 LF|換行 LF|Newline LF|改行 LF|Zeilenumbruch LF
换行 CRLF|換行 CRLF|Newline CRLF|改行 CRLF|Zeilenumbruch CRLF
不换行|不換行|No newline|改行なし|Kein Zeilenumbruch
发送|傳送|Send|送信|Senden
清空显示|清空顯示|Clear display|表示を消去|Anzeige leeren
最近运行记录|最近執行記錄|Recent activity|最近の動作|Letzte Aktivitäten
导出日志|匯出日誌|Export logs|ログを書き出す|Protokolle exportieren
真实连接、操作结果与软件错误。|實際連線、操作結果與軟體錯誤。|Connections, operation results and app errors.|接続、操作結果、アプリのエラー。|Verbindungen, Ergebnisse und App-Fehler.
设备文件的真实下载进度。|裝置檔案的實際下載進度。|Download progress for device files.|デバイスファイルのダウンロード状況。|Downloadfortschritt der Gerätedateien.
打开所在文件夹|開啟所在資料夾|Show in folder|フォルダーを開く|Im Ordner anzeigen
本次运行还没有下载记录。|本次執行尚無下載記錄。|No downloads in this session.|ダウンロード履歴はありません。|Noch keine Downloads in dieser Sitzung.
SD 卡存储|SD 卡儲存|SD card storage|SD カード|SD-Kartenspeicher
新建文件夹|新增資料夾|New folder|新しいフォルダー|Neuer Ordner
上传文件|上傳檔案|Upload files|ファイルをアップロード|Dateien hochladen
上传文件夹|上傳資料夾|Upload folder|フォルダーをアップロード|Ordner hochladen
下载|下載|Download|ダウンロード|Herunterladen
复制|複製|Copy|コピー|Kopieren
剪切|剪下|Cut|切り取り|Ausschneiden
粘贴|貼上|Paste|貼り付け|Einfügen
删除|刪除|Delete|削除|Löschen
上传|上傳|Upload|アップロード|Hochladen
移动|移動|Move|移動|Verschieben
传输队列|傳輸佇列|Transfers|転送キュー|Übertragungen
SD 卡|SD 卡|SD card|SD カード|SD-Karte
此设备|此裝置|This device|このデバイス|Dieses Gerät
文件与设备实时同步|檔案與裝置即時同步|Live device files|デバイスのファイルを表示|Dateien direkt vom Gerät
上级目录|上層資料夾|Parent folder|親フォルダー|Übergeordneter Ordner
双击编辑路径|按兩下編輯路徑|Double-click to edit path|ダブルクリックでパスを編集|Doppelklick zum Bearbeiten des Pfads
编辑路径|編輯路徑|Edit path|パスを編集|Pfad bearbeiten
目录路径|目錄路徑|Folder path|フォルダーのパス|Ordnerpfad
搜索当前文件夹|搜尋目前資料夾|Search this folder|このフォルダーを検索|Diesen Ordner durchsuchen
名称|名稱|Name|名前|Name
类型|類型|Type|種類|Typ
大小|大小|Size|サイズ|Größe
修改时间|修改時間|Modified|更新日時|Geändert
全选|全選|Select all|すべて選択|Alles auswählen
文件夹|資料夾|Folder|フォルダー|Ordner
文件|檔案|File|ファイル|Datei
正在读取文件…|正在讀取檔案…|Reading files…|ファイルを取得中…|Dateien werden gelesen…
没有匹配的文件|沒有符合的檔案|No matching files|一致するファイルがありません|Keine passenden Dateien
此文件夹为空|此資料夾為空|This folder is empty|空のフォルダーです|Dieser Ordner ist leer
可以拖入文件，或点击“上传文件”|可拖入檔案，或按「上傳檔案」|Drop files here or click Upload files|ファイルをドラッグするかアップロードを選択|Dateien hier ablegen oder „Dateien hochladen“ wählen
拖入电脑文件或文件夹，上传到当前目录|拖入電腦檔案或資料夾，上傳至目前目錄|Drop PC files or folders to upload here|PC のファイルやフォルダーをここにドラッグ|PC-Dateien oder -Ordner zum Hochladen hier ablegen
松开以开始上传|放開以開始上傳|Release to upload|ドロップしてアップロード|Zum Hochladen loslassen
Ctrl 多选 · Shift 连选 · 右键操作|Ctrl 多選 · Shift 連選 · 右鍵操作|Ctrl: multi-select · Shift: range · Right-click: actions|Ctrl：複数選択 · Shift：範囲選択 · 右クリック：操作|Strg: Mehrfachauswahl · Umschalt: Bereich · Rechtsklick: Aktionen
打开 / 预览|開啟 / 預覽|Open / Preview|開く / プレビュー|Öffnen / Vorschau
复制  Ctrl+C|複製  Ctrl+C|Copy  Ctrl+C|コピー  Ctrl+C|Kopieren  Strg+C
剪切  Ctrl+X|剪下  Ctrl+X|Cut  Ctrl+X|切り取り  Ctrl+X|Ausschneiden  Strg+X
粘贴  Ctrl+V|貼上  Ctrl+V|Paste  Ctrl+V|貼り付け  Ctrl+V|Einfügen  Strg+V
重命名  F2|重新命名  F2|Rename  F2|名前を変更  F2|Umbenennen  F2
删除  Delete|刪除  Delete|Delete  Delete|削除  Delete|Löschen  Entf
取消|取消|Cancel|キャンセル|Abbrechen
确定|確定|OK|OK|OK
保存|儲存|Save|保存|Speichern
已复制，可进入目标目录粘贴|已複製，可進入目標目錄貼上|Copied. Open a destination folder and paste.|コピーしました。移動先で貼り付けてください。|Kopiert. Im Zielordner einfügen.
已剪切，可进入目标目录粘贴|已剪下，可進入目標目錄貼上|Cut. Open a destination folder and paste.|切り取りました。移動先で貼り付けてください。|Ausgeschnitten. Im Zielordner einfügen.
请返回原设备粘贴；跨设备请先下载再上传|請返回原裝置貼上；跨裝置請先下載再上傳|Paste on the original device. For another device, download then upload.|元のデバイスで貼り付けてください。別のデバイスへはダウンロード後にアップロードしてください。|Auf dem ursprünglichen Gerät einfügen. Für andere Geräte zuerst herunter- und dann hochladen.
排队中|排隊中|Queued|待機中|In Warteschlange
传输中|傳輸中|Transferring|転送中|Übertragung läuft
准备中|準備中|Preparing|準備中|Vorbereitung
正在保存|正在儲存|Saving on device|デバイスに保存中|Wird auf dem Gerät gespeichert
等待选择|等待選擇|Waiting for choice|選択待ち|Warte auf Auswahl
已完成|已完成|Completed|完了|Abgeschlossen
已取消|已取消|Cancelled|キャンセル済み|Abgebrochen
失败|失敗|Failed|失敗|Fehlgeschlagen
已跳过|已略過|Skipped|スキップ済み|Übersprungen
所有任务已停止|所有工作已停止|No active transfers|転送は停止しています|Keine aktiven Übertragungen
顺序传输|依序傳輸|Sequential transfers|順次転送|Sequenzielle Übertragung
统计中|統計中|Calculating|計算中|Berechnung läuft
文件明细|檔案明細|File details|ファイルの詳細|Dateidetails
重试|重試|Retry|再試行|Wiederholen
打开保存位置|開啟儲存位置|Open destination|保存先を開く|Zielordner öffnen
暂无传输任务|尚無傳輸工作|No transfers|転送はありません|Keine Übertragungen
永久删除|永久刪除|Delete permanently|完全に削除|Endgültig löschen
设备没有回收站，文件夹内的内容也会删除。|裝置沒有資源回收筒，資料夾內容也會刪除。|There is no recycle bin. Folder contents will also be deleted.|ごみ箱はありません。フォルダー内も削除されます。|Kein Papierkorb vorhanden. Ordnerinhalte werden ebenfalls gelöscht.
目标已存在|目標已存在|Destination exists|保存先が存在します|Ziel ist vorhanden
覆盖将合并文件夹，并逐个处理同名文件。|覆寫將合併資料夾，並逐一處理同名檔案。|Overwrite merges folders and checks each conflicting file.|上書きではフォルダーを結合し、同名ファイルを個別に確認します。|Überschreiben führt Ordner zusammen und prüft gleichnamige Dateien einzeln.
覆盖|覆寫|Overwrite|上書き|Überschreiben
跳过|略過|Skip|スキップ|Überspringen
自动改名|自動重新命名|Auto-rename|自動で名前を変更|Automatisch umbenennen
取消任务|取消工作|Cancel task|タスクを中止|Aufgabe abbrechen
应用到本次任务的全部冲突|套用至本次工作的所有衝突|Apply to all conflicts in this task|このタスクのすべての重複に適用|Auf alle Konflikte dieser Aufgabe anwenden
选择上传文件夹|選擇上傳資料夾|Choose folders to upload|アップロードするフォルダーを選択|Ordner zum Hochladen auswählen
选择上传文件|選擇上傳檔案|Choose files to upload|アップロードするファイルを選択|Dateien zum Hochladen auswählen
选择下载保存位置|選擇下載儲存位置|Choose download destination|ダウンロード先を選択|Download-Ziel auswählen
文件预览|檔案預覽|File preview|ファイルのプレビュー|Dateivorschau
文件编辑|檔案編輯|File editor|ファイルエディター|Dateieditor
保存 Ctrl+S|儲存 Ctrl+S|Save Ctrl+S|保存 Ctrl+S|Speichern Strg+S
另存为|另存新檔|Save as|名前を付けて保存|Speichern unter
文件内容|檔案內容|File content|ファイルの内容|Dateiinhalt
设备图片|裝置圖片|Device image|デバイスの画像|Gerätebild
另存为设备文件|另存為裝置檔案|Save as device file|デバイスに別名で保存|Als Gerätedatei speichern
设备文件路径|裝置檔案路徑|Device file path|デバイスのファイルパス|Gerätedateipfad
请输入以 /sd/ 开头的完整文件路径|請輸入以 /sd/ 開頭的完整檔案路徑|Enter a full path starting with /sd/|/sd/ で始まる完全なパスを入力してください|Vollständigen Pfad beginnend mit /sd/ eingeben
UTF-8 · 文本编辑|UTF-8 · 文字編輯|UTF-8 · Text editor|UTF-8 · テキスト編集|UTF-8 · Texteditor
未修改|未修改|Unchanged|変更なし|Unverändert
未保存的修改|尚未儲存的修改|Unsaved changes|未保存の変更|Ungespeicherte Änderungen
已保存|已儲存|Saved|保存済み|Gespeichert
图片预览|圖片預覽|Image preview|画像プレビュー|Bildvorschau
文件尚未保存，放弃修改并关闭？|檔案尚未儲存，放棄修改並關閉？|Discard unsaved changes and close?|未保存の変更を破棄して閉じますか？|Ungespeicherte Änderungen verwerfen und schließen?
继续编辑|繼續編輯|Keep editing|編集を続ける|Weiter bearbeiten
放弃修改|放棄修改|Discard changes|変更を破棄|Änderungen verwerfen
关闭控制台？|關閉控制台？|Close the console?|コンソールを閉じますか？|Konsole schließen?
进行中的文件传输将取消。|進行中的檔案傳輸將取消。|Active file transfers will be cancelled.|進行中の転送は中止されます。|Laufende Übertragungen werden abgebrochen.
继续使用|繼續使用|Keep open|開いたままにする|Geöffnet lassen
设备文件已被其他操作修改，仍要覆盖？|裝置檔案已被其他操作修改，仍要覆寫？|The device file changed elsewhere. Overwrite anyway?|別の操作で変更されたファイルを上書きしますか？|Die Gerätedatei wurde anderweitig geändert. Trotzdem überschreiben?
同名文件已存在，是否覆盖？|同名檔案已存在，是否覆寫？|A file with this name exists. Overwrite it?|同名のファイルを上書きしますか？|Eine gleichnamige Datei ist vorhanden. Überschreiben?
请等待传输队列结束后保存编辑|請等待傳輸佇列結束後儲存編輯|Wait for transfers to finish before saving|転送完了後に保存してください|Vor dem Speichern das Ende der Übertragung abwarten
同名项目已存在|同名項目已存在|An item with this name already exists|同名の項目が存在します|Ein gleichnamiger Eintrag ist bereits vorhanden
请等待传输队列结束后修改目录|請等待傳輸佇列結束後修改目錄|Wait for transfers before changing folders|転送完了後にフォルダーを変更してください|Vor Ordneränderungen das Übertragungsende abwarten
请等待传输队列结束后重命名|請等待傳輸佇列結束後重新命名|Wait for transfers before renaming|転送完了後に名前を変更してください|Vor dem Umbenennen das Übertragungsende abwarten
文件路径无效|檔案路徑無效|Invalid file path|無効なファイルパス|Ungültiger Dateipfad
仅能访问设备开放的 SD 卡|只能存取裝置開放的 SD 卡|Only the device SD card is accessible|デバイスの SD カードのみアクセスできます|Nur auf die SD-Karte des Geräts kann zugegriffen werden
名称包含不支持的字符或过长|名稱包含不支援的字元或過長|The name is too long or contains unsupported characters|名前が長すぎるか、使用できない文字が含まれます|Name zu lang oder mit unzulässigen Zeichen
此操作需要设备安装 DevTools 服务|此操作需要裝置安裝 DevTools 服務|Install DevTools on the device for this operation|この操作には DevTools が必要です|Für diesen Vorgang muss DevTools auf dem Gerät installiert sein
DevTools 未能启动|DevTools 未能啟動|Could not start DevTools|DevTools を起動できません|DevTools konnte nicht gestartet werden
DevTools 文件接口不可用，请在设备中更新 DevTools|DevTools 檔案介面無法使用，請在裝置更新 DevTools|DevTools file API unavailable. Update DevTools on the device.|DevTools のファイル API が利用できません。更新してください。|DevTools-Dateischnittstelle nicht verfügbar. DevTools auf dem Gerät aktualisieren.
不能删除 SD 卡根目录|無法刪除 SD 卡根目錄|Cannot delete the SD card root|SD カードのルートは削除できません|SD-Kartenstamm kann nicht gelöscht werden
不能移动根目录或移入自身|無法移動根目錄或移入自身|Cannot move the root or move a folder into itself|ルートやフォルダー自身には移動できません|Stammordner kann nicht verschoben werden; kein Verschieben in sich selbst
请先选择文件|請先選擇檔案|Select files first|ファイルを選択してください|Zuerst Dateien auswählen
文件过大，请下载后打开（文本上限 2 MB，图片 12 MB）|檔案過大，請下載後開啟（文字上限 2 MB，圖片 12 MB）|File too large. Download to open (text: 2 MB, images: 12 MB).|大きすぎるためダウンロードしてください（テキスト 2 MB、画像 12 MB）。|Datei zu groß. Herunterladen und öffnen (Text: 2 MB, Bilder: 12 MB).
此文件不是文本，请下载后打开|此檔案不是文字，請下載後開啟|This is not a text file. Download it to open.|テキストではありません。ダウンロードして開いてください。|Keine Textdatei. Zum Öffnen herunterladen.
下载大小与目录记录不一致，请刷新后重试|下載大小與目錄記錄不符，請重新整理後再試|File size changed. Refresh and retry.|ファイルサイズが変わりました。更新して再試行してください。|Dateigröße geändert. Aktualisieren und erneut versuchen.
设备保存的文件大小不一致|裝置儲存的檔案大小不符|Saved file size does not match|保存したファイルのサイズが一致しません|Gespeicherte Dateigröße stimmt nicht überein
设备传输超时|裝置傳輸逾時|Transfer timed out|転送がタイムアウトしました|Zeitüberschreitung bei der Übertragung
设备上传超时|裝置上傳逾時|Upload timed out|アップロードがタイムアウトしました|Zeitüberschreitung beim Hochladen
默认值已填入，点击保存设置后生效|已填入預設值，按「儲存設定」後生效|Defaults filled in. Click Save settings to apply.|初期値を入力しました。設定を保存すると適用されます。|Standardwerte eingetragen. Mit Einstellungen speichern anwenden.
设置已修改，等待保存|設定已修改，等待儲存|Settings changed; not saved yet|設定は変更されましたが未保存です|Einstellungen geändert, noch nicht gespeichert
设置已保存到设备|設定已儲存至裝置|Settings saved to device|デバイスに設定を保存しました|Einstellungen auf dem Gerät gespeichert
更新源已保存|更新來源已儲存|Update feed saved|更新フィードを保存しました|Update-Feed gespeichert
正在检查更新…|正在檢查更新…|Checking for updates…|更新を確認中…|Updates werden gesucht…
当前已是最新版本|目前已是最新版本|You are up to date|最新バージョンです|Bereits auf dem neuesten Stand
下载更新|下載更新|Download update|更新をダウンロード|Update herunterladen
安装并重启|安裝並重新啟動|Install and restart|インストールして再起動|Installieren und neu starten
设备已添加|裝置已新增|Device added|デバイスを追加しました|Gerät hinzugefügt
设备不存在|裝置不存在|Device not found|デバイスが見つかりません|Gerät nicht gefunden
名称已保存|名稱已儲存|Name saved|名前を保存しました|Name gespeichert
语言设置已保存|語言設定已儲存|Language saved|言語を保存しました|Sprache gespeichert
音乐同步 SMTC Music|音樂同步 SMTC Music|Music sync · SMTC Music|音楽同期 · SMTC Music|Musiksynchronisierung · SMTC Music
电脑性能监控|電腦效能監控|PC performance monitor|PC パフォーマンス監視|PC-Leistungsüberwachung
桌面投屏|桌面投影|Desktop mirror|画面転送|Bildschirmübertragung
接收工作状态事件并推送到设备|接收工作狀態事件並推送至裝置|Forward task status events to the device|タスクの状態をデバイスに送信|Aufgabenstatus an das Gerät senden
仅返回状态和时间，不传输会话正文|僅回傳狀態與時間，不傳輸對話正文|Send status and time only, without chat content|状態と時刻のみ送信し、会話内容は送信しません|Nur Status und Zeit senden, keine Chatinhalte
全部|全部|All|すべて|Alle
音乐同步|音樂同步|Music sync|音楽同期|Musiksynchronisierung
电脑监控|電腦監控|PC monitor|PC モニター|PC-Monitor
当前播放信息、歌词、封面和媒体控制|目前播放資訊、歌詞、封面和媒體控制|Playback info, lyrics, artwork and media controls|再生情報、歌詞、アートワーク、メディア操作|Wiedergabeinfo, Liedtexte, Cover und Mediensteuerung
CPU、GPU、内存、温度与网络数据|CPU、GPU、記憶體、溫度與網路資料|CPU, GPU, memory, temperature and network data|CPU、GPU、メモリ、温度、ネットワーク情報|CPU-, GPU-, Speicher-, Temperatur- und Netzwerkdaten
显示器或指定区域|顯示器或指定區域|Display or selected area|画面または指定領域|Monitor oder ausgewählter Bereich
Codex 事件类型与工作状态|Codex 事件類型與工作狀態|Codex event types and task status|Codex イベント種別とタスク状態|Codex-Ereignistypen und Aufgabenstatus
设备 Web 控制|裝置 Web 控制|Device web control|デバイス Web 操作|Web-Gerätesteuerung
电脑服务已重新配置|電腦服務已重新設定|PC service reconfigured|PC サービスを再設定しました|PC-Dienst neu konfiguriert
传输任务筛选|傳輸工作篩選|Filter transfers|転送の絞り込み|Übertragungen filtern
文件编辑|檔案編輯|File editor|ファイル編集|Dateieditor
进行中|進行中|In progress|進行中|Aktiv
已结束|已結束|Finished|終了|Beendet
已传输|已傳輸|Transferred|転送済み|Übertragen
传输速度|傳輸速度|Transfer speed|転送速度|Geschwindigkeit
剩余时间|剩餘時間|Time remaining|残り時間|Restzeit
等待开始|等待開始|Waiting to start|開始待ち|Wartet auf Start
当前文件|目前檔案|Current file|現在のファイル|Aktuelle Datei
正在统计文件|正在統計檔案|Counting files|ファイルを集計中|Dateien werden gezählt
此分类暂无任务|此分類沒有工作|No tasks in this category|この分類にタスクはありません|Keine Aufgaben in dieser Kategorie
进入设备应用|進入裝置應用|Open on device|デバイスで起動|Auf Gerät öffnen
对应应用|對應應用|Device app|対応アプリ|Geräte-App
设备未安装对应应用|裝置未安裝對應應用|Matching app is not installed on the device|対応アプリがデバイスにインストールされていません|Zugehörige App ist auf dem Gerät nicht installiert
当前设备|目前裝置|Current device|現在のデバイス|Aktuelles Gerät
任务按顺序执行，关闭面板后继续传输|工作依序執行，關閉面板後繼續傳輸|Tasks run in order and continue when this panel is closed|順番に処理し、パネルを閉じても転送を続けます|Aufgaben laufen nacheinander und auch bei geschlossenem Panel weiter
设备串口日志|裝置序列埠記錄|Device serial logs|デバイスのシリアルログ|Geräteprotokolle über COM
连接设备时自动记录串口|連線裝置時自動記錄序列埠|Automatically connect and log serial output|接続時にシリアルログを自動記録|Serielle Ausgabe automatisch aufzeichnen
串口日志文件夹|序列埠記錄資料夾|Serial log folder|シリアルログのフォルダー|Ordner für serielle Protokolle
自动保存串口输出，识别死机和重启并保留异常前后文。|自動儲存序列埠輸出，識別當機與重新啟動並保留異常前後文。|Save serial output and capture crash and restart context.|シリアル出力を保存し、クラッシュと再起動の前後を記録します。|Serielle Ausgabe und Kontext von Abstürzen und Neustarts speichern.
单个可用串口自动连接；多个串口时，选择下方串口并连接以记住设备绑定。断开按钮暂停本次自动连接。|單一可用序列埠自動連線；多個時選取下方序列埠以記住裝置綁定。中斷連線會暫停本次自動連線。|One available port connects automatically. With multiple ports, select and connect once to bind the device. Disconnect pauses automatic connection for this session.|ポートが1つなら自動接続します。複数ある場合は選択して接続すると保存されます。切断すると今回は自動接続を停止します。|Ein verfügbarer Port wird automatisch verbunden. Bei mehreren Ports einmal auswählen und verbinden. Trennen pausiert die automatische Verbindung für diese Sitzung.
设备死机|裝置當機|Device crash|デバイスのクラッシュ|Geräteabsturz
设备重启|裝置重新啟動|Device restart|デバイスの再起動|Geräteneustart
设备异常重启|裝置異常重新啟動|Abnormal device restart|異常による再起動|Unerwarteter Geräteneustart
等待 USB 串口|等待 USB 序列埠|Waiting for USB serial port|USB シリアルポートを待機中|Warten auf USB-COM-Port
正在记录串口日志|正在記錄序列埠輸出|Recording serial output|シリアルログを記録中|Serielle Ausgabe wird aufgezeichnet
自动串口记录已关闭|自動序列埠記錄已關閉|Automatic serial logging is off|シリアル自動記録は無効です|Automatische serielle Aufzeichnung ist aus
串口记录已暂停，点击连接可恢复|序列埠記錄已暫停，按連線可繼續|Logging paused. Connect to resume.|記録は一時停止中です。接続すると再開します。|Aufzeichnung pausiert. Zum Fortsetzen verbinden.
多个设备或串口，请选择串口并连接以绑定|多個裝置或序列埠，請選取並連線以綁定|Multiple devices or ports. Select a port and connect to bind.|複数のデバイスまたはポートがあります。選択して接続してください。|Mehrere Geräte oder Ports. Port auswählen und zum Zuordnen verbinden.
USB 串口中断，等待自动重连|USB 序列埠中斷，等待自動重新連線|USB serial disconnected; waiting to reconnect|USB シリアル切断。自動再接続を待機中|USB-Verbindung unterbrochen; automatische Wiederverbindung ausstehend
应用正在启动，请稍候|應用程式正在啟動，請稍候|An app is starting. Please wait.|アプリを起動中です。お待ちください。|Eine App wird gestartet. Bitte warten.
`;
 const table=Object.fromEntries(rows.trim().split('\n').map(line=>{const [key,...values]=line.split('|');return [key,values];}));
 const patterns=[];
 function pattern(regex,translations){patterns.push([regex,translations]);}
 pattern(/^查看全部 (\d+) 个(?: ›)?$/,['查看全部 {0} 個 ›','View all {0} ›','全 {0} 件を表示 ›','Alle {0} anzeigen ›']);
 pattern(/^(\d+) 个项目$/,['{0} 個項目','{0} items','{0} 項目','{0} Elemente']);
 pattern(/^已选择 (\d+) 项$/,['已選取 {0} 項','{0} selected','{0} 項目を選択','{0} ausgewählt']);
 pattern(/^(\d+) 项$/,['{0} 項','{0} items','{0} 項目','{0} Elemente']);
 pattern(/^已复制 (\d+) 项$/,['已複製 {0} 項','{0} copied','{0} 項目をコピー','{0} kopiert']);
 pattern(/^待移动 (\d+) 项$/,['待移動 {0} 項','{0} ready to move','{0} 項目を移動予定','{0} zum Verschieben']);
 pattern(/^正在处理 (\d+) 个任务$/,['正在處理 {0} 個工作','Processing {0} tasks','{0} 件を処理中','{0} Aufgaben werden bearbeitet']);
 pattern(/^跳过 (\d+) 项$/,['略過 {0} 項','{0} skipped','{0} 項目をスキップ','{0} übersprungen']);
 pattern(/^剩余约 (\d+) 秒$/,['剩餘約 {0} 秒','About {0} s left','残り約 {0} 秒','Noch etwa {0} Sek.']);
 pattern(/^剩余约 (\d+) 分钟$/,['剩餘約 {0} 分鐘','About {0} min left','残り約 {0} 分','Noch etwa {0} Min.']);
 pattern(/^传输\s*(\d*)$/,['傳輸 {0}','Transfers {0}','転送 {0}','Übertragungen {0}']);
 pattern(/^永久删除选中的 (\d+) 个项目？$/,['永久刪除選取的 {0} 個項目？','Permanently delete {0} selected items?','選択した {0} 項目を完全に削除しますか？','{0} ausgewählte Elemente endgültig löschen?']);
 pattern(/^(.+) 已存在$/,['{0} 已存在','{0} already exists','{0} は既に存在します','{0} ist bereits vorhanden']);
 pattern(/^(\d+) 个文件的未保存修改将丢弃。$/,['{0} 個檔案尚未儲存的修改將捨棄。','Unsaved changes in {0} files will be discarded.','{0} ファイルの未保存の変更を破棄します。','Ungespeicherte Änderungen in {0} Dateien werden verworfen.']);
 pattern(/^([A-Z0-9]+) 文件$/,['{0} 檔案','{0} file','{0} ファイル','{0}-Datei']);
 pattern(/^读取目录失败：(.*)$/,['讀取目錄失敗：{0}','Could not read folder: {0}','フォルダーを読み込めません：{0}','Ordner konnte nicht gelesen werden: {0}']);
 pattern(/^连接失败：(.*)$/,['連線失敗：{0}','Connection failed: {0}','接続に失敗しました：{0}','Verbindung fehlgeschlagen: {0}']);
 pattern(/^等待已绑定串口 (COM\d+)$/,['等待已綁定序列埠 {0}','Waiting for bound port {0}','登録ポート {0} を待機中','Warten auf zugeordneten Port {0}']);
 pattern(/^端口 (\d+)$/,['連接埠 {0}','Port {0}','ポート {0}','Port {0}']);
 pattern(/^(.+)设置$/,['{0}設定','{0} settings','{0} 設定','{0}-Einstellungen']);
 pattern(/^显示器 (\d+)(.*)$/,['顯示器 {0}{1}','Display {0}{1}','ディスプレイ {0}{1}','Monitor {0}{1}']);
 pattern(/^主显示器 (\d+)(.*)$/,['主顯示器 {0}{1}','Primary display {0}{1}','メイン画面 {0}{1}','Hauptmonitor {0}{1}']);
 function translate(value,language='zh-CN'){
  if(typeof value!=='string'||language==='zh-CN'||!codes.includes(language))return value;
  const index=codes.indexOf(language)-1,source=value.trim(),padding=[value.slice(0,value.indexOf(source)),value.slice(value.indexOf(source)+source.length)];
  let result=table[source]?.[index];
  if(result===undefined){for(const [regex,translations] of patterns){const m=source.match(regex);if(m){result=translations[index].replace(/\{(\d+)\}/g,(_,n)=>m[Number(n)+1]||'');break;}}}
  if(result===undefined&&/ · |\n/.test(source))result=source.split(/( · |\n)/).map(s=>s===' · '||s==='\n'?s:translate(s,language)).join('');
  if(result===undefined&&/[↑↓]$/.test(source))result=translate(source.slice(0,-1).trim(),language)+' '+source.slice(-1);
  return result===undefined?value:padding[0]+result+padding[1];
 }
 function localizedDialog(dialog,getLanguage){return new Proxy(dialog,{get(target,key){if(!['showMessageBox','showOpenDialog','showSaveDialog'].includes(key))return Reflect.get(target,key);return (...args)=>{const options=args[args.length-1],lang=getLanguage();const next={...options};for(const field of ['title','message','detail','checkboxLabel','buttonLabel'])if(typeof next[field]==='string')next[field]=translate(next[field],lang);if(next.buttons)next.buttons=next.buttons.map(s=>translate(s,lang));if(next.filters)next.filters=next.filters.map(f=>({...f,name:translate(f.name,lang)}));args[args.length-1]=next;return target[key](...args);};}});}
 return {codes,translate,localizedDialog};
});
