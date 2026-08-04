(() => {
  const rows = [
    ['总览','Overview','概要','總覽'], ['添加设备','Add device','デバイスを追加','新增裝置'], ['设备','Devices','デバイス','裝置'], ['设备控制','Device control','デバイス制御','裝置控制'], ['电脑服务','PC services','PC サービス','電腦服務'], ['运行日志','Logs','実行ログ','執行日誌'], ['关于','About','情報','關於'], ['等待数据','Waiting for data','データ待機中','等待資料'], ['请选择一台设备','Select a device','デバイスを選択してください','請選擇一台裝置'],
    ['电脑网络','PC network','PC ネットワーク','電腦網路'], ['正在检测','Checking','確認中','正在偵測'], ['当前设备','Current device','現在のデバイス','目前裝置'], ['未选择设备','No device selected','デバイス未選択','未選擇裝置'], ['设备未连接','Device offline','デバイス未接続','裝置未連線'], ['进入设备控制','Open device control','デバイス制御を開く','進入裝置控制'],
    ['设备总览','Device overview','デバイス概要','裝置總覽'], ['统一连接并管理 Clocteck Cubic','Connect and manage Clocteck Cubic devices','Clocteck Cubic を接続・管理','統一連接並管理 Clocteck Cubic'], ['一处管理设备、应用与电脑端服务。','Manage devices, apps, and PC services in one place.','デバイス、アプリ、PC サービスを一括管理。','集中管理裝置、應用程式與電腦端服務。'], ['完成首次配网、按 IP 管理多台设备，并为 Holopet、Codex Buddy、全息 PC 监控和桌面投屏提供统一入口。','Set up Wi-Fi, manage multiple devices by IP, and run all companion services from one place.','Wi-Fi 設定、IP 別の複数デバイス管理、各種 PC サービスを一つにまとめます。','完成配網、依 IP 管理多台裝置，並統一執行電腦端服務。'], ['添加新设备','Add a device','デバイスを追加','新增裝置'], ['扫描局域网','Scan LAN','LAN をスキャン','掃描區域網路'], ['内存','Memory','メモリ','記憶體'], ['当前 Wi-Fi','Current Wi-Fi','現在の Wi-Fi','目前 Wi-Fi'], ['当前设备','Current device','現在のデバイス','目前裝置'], ['尚未选择','Not selected','未選択','尚未選擇'], ['IP 用于区分设备','IP identifies each device','IP でデバイスを識別','以 IP 區分裝置'], ['设备在线','Device online','デバイスはオンライン','裝置在線'], ['设备暂未响应','Device not responding','デバイス応答なし','裝置暫無回應'], ['管理全部 →','Manage all →','すべて管理 →','管理全部 →'], ['设备应用需要的后台接口','Background APIs required by device apps','デバイスアプリ用バックグラウンド API','裝置應用程式所需的背景介面'],
    ['首次连接向导','FIRST-TIME SETUP','初回接続ガイド','首次連線精靈'], ['连接 Clocteck Cubic','Connect Clocteck Cubic','Clocteck Cubic に接続','連接 Clocteck Cubic'], ['准备连接设备','Ready to connect','接続準備完了','準備連接裝置'], ['程序会保存当前 Wi-Fi，再连接设备热点并打开配网页面。','The app saves the current Wi-Fi, connects to the device hotspot, and opens setup.','現在の Wi-Fi を保存し、デバイス AP に接続して設定画面を開きます。','程式會保存目前 Wi-Fi，再連接裝置熱點並開啟配網頁面。'], ['点击扫描，查找附近的','Scan for nearby','スキャンして近くの','點擊掃描，尋找附近的'], ['设备热点。','device hotspots.','デバイス AP を検索します。','裝置熱點。'], ['扫描设备热点','Scan device hotspots','デバイス AP をスキャン','掃描裝置熱點'], ['已完成配置','Setup complete','設定完了','已完成設定'], ['取消并恢复网络','Cancel and restore network','キャンセルしてネットワークを復元','取消並恢復網路'], ['配网期间电脑短暂断网属于正常现象。','A brief PC network interruption during setup is normal.','設定中に PC のネットワークが一時切断されるのは正常です。','配網期間電腦短暫斷網屬於正常現象。'], ['设备加入目标 Wi-Fi 后，软件会恢复电脑网络、按 IP 发现设备并进入内置控制界面。','After setup, the app restores PC Wi-Fi, finds the device by IP, and opens built-in controls.','設定後、PC の Wi-Fi を復元し、IP でデバイスを検出して内蔵制御画面を開きます。','裝置加入目標 Wi-Fi 後，軟體會恢復電腦網路、依 IP 發現裝置並進入內建控制介面。'], ['连接步骤','Connection steps','接続手順','連接步驟'], ['发现设备热点','Find device hotspot','デバイス AP を検出','發現裝置熱點'], ['连接设备','Connect device','デバイスに接続','連接裝置'], ['切换到设备配置网络','Switch to the setup network','設定ネットワークに切り替え','切換到裝置設定網路'], ['设置目标 Wi-Fi','Set target Wi-Fi','接続先 Wi-Fi を設定','設定目標 Wi-Fi'], ['在设备网页中填写','Complete on the device page','デバイス画面で入力','在裝置網頁中填寫'], ['恢复电脑网络','Restore PC network','PC ネットワークを復元','恢復電腦網路'], ['重新连接原 Wi-Fi','Reconnect original Wi-Fi','元の Wi-Fi に再接続','重新連接原 Wi-Fi'], ['进入内置控制','Open built-in controls','内蔵制御を開く','進入內建控制'], ['使用发现到的设备 IP','Use the discovered device IP','検出したデバイス IP を使用','使用發現到的裝置 IP'],
    ['扫描 clocteck-cubic','Scan clocteck-cubic','clocteck-cubic をスキャン','掃描 clocteck-cubic'], ['简体中文','Simplified Chinese','簡体字中国語','簡體中文'], ['日本語','Japanese','日本語','日文'], ['繁體中文','Traditional Chinese','繁体字中国語','繁體中文'],
    ['欧洲中部（UTC+1）','Central Europe (UTC+1)','中央ヨーロッパ（UTC+1）','中歐（UTC+1）'], ['东欧（UTC+2）','Eastern Europe (UTC+2)','東ヨーロッパ（UTC+2）','東歐（UTC+2）'], ['莫斯科（UTC+3）','Moscow (UTC+3)','モスクワ（UTC+3）','莫斯科（UTC+3）'], ['海湾（UTC+4）','Gulf (UTC+4)','湾岸（UTC+4）','海灣（UTC+4）'], ['巴基斯坦（UTC+5）','Pakistan (UTC+5)','パキスタン（UTC+5）','巴基斯坦（UTC+5）'], ['印度（UTC+5:30）','India (UTC+5:30)','インド（UTC+5:30）','印度（UTC+5:30）'], ['孟加拉（UTC+6）','Bangladesh (UTC+6)','バングラデシュ（UTC+6）','孟加拉（UTC+6）'], ['曼谷（UTC+7）','Bangkok (UTC+7)','バンコク（UTC+7）','曼谷（UTC+7）'], ['中国标准时间（UTC+8）','China Standard Time (UTC+8)','中国標準時（UTC+8）','中國標準時間（UTC+8）'], ['日本（UTC+9）','Japan (UTC+9)','日本（UTC+9）','日本（UTC+9）'], ['澳大利亚东部（UTC+10）','Australia Eastern (UTC+10)','オーストラリア東部（UTC+10）','澳洲東部（UTC+10）'], ['新西兰（UTC+12）','New Zealand (UTC+12)','ニュージーランド（UTC+12）','紐西蘭（UTC+12）'], ['夏威夷（UTC-10）','Hawaii (UTC-10)','ハワイ（UTC-10）','夏威夷（UTC-10）'], ['阿拉斯加（UTC-9/-8）','Alaska (UTC-9/-8)','アラスカ（UTC-9/-8）','阿拉斯加（UTC-9/-8）'], ['北美太平洋（UTC-8/-7）','North America Pacific (UTC-8/-7)','北米太平洋（UTC-8/-7）','北美太平洋（UTC-8/-7）'], ['北美山地（UTC-7/-6）','North America Mountain (UTC-7/-6)','北米山岳部（UTC-7/-6）','北美山區（UTC-7/-6）'], ['北美中部（UTC-6/-5）','North America Central (UTC-6/-5)','北米中部（UTC-6/-5）','北美中部（UTC-6/-5）'], ['北美东部（UTC-5/-4）','North America Eastern (UTC-5/-4)','北米東部（UTC-5/-4）','北美東部（UTC-5/-4）'], ['巴西利亚（UTC-3）','Brasilia (UTC-3)','ブラジリア（UTC-3）','巴西利亞（UTC-3）'],
    ['我的设备','My devices','マイデバイス','我的裝置'], ['IP 地址是设备的唯一标识，可同时保存多台设备','Each IP identifies one device; multiple devices can be saved','IP アドレスで複数デバイスを識別・保存します','IP 位址是裝置的唯一識別，可同時保存多台裝置'], ['手动连接','MANUAL CONNECTION','手動接続','手動連接'], ['输入设备 IPv4 地址','Enter device IPv4 address','デバイス IPv4 アドレスを入力','輸入裝置 IPv4 位址'], ['适用于 mDNS 不可用或同一局域网有多台设备的情况。','Use when mDNS is unavailable or multiple devices share the LAN.','mDNS が使えない場合や複数デバイスが同じ LAN にある場合に使用します。','適用於 mDNS 無法使用或同一區域網路有多台裝置。'], ['例如 192.168.0.188','For example 192.168.0.188','例：192.168.0.188','例如 192.168.0.188'], ['连接设备','Connect','接続','連接裝置'], ['尚未保存设备，可扫描局域网或手动输入 IP。','No saved devices. Scan the LAN or enter an IP manually.','保存済みデバイスはありません。LAN をスキャンするか IP を入力してください。','尚未保存裝置，可掃描區域網路或手動輸入 IP。'], ['已保存设备','Saved device','保存済みデバイス','已保存裝置'], ['在线','Online','オンライン','在線'], ['未连接','Offline','オフライン','未連線'], ['进入控制','Open controls','制御を開く','進入控制'], ['设为当前','Select','選択','設為目前'], ['配置 PC 监控','Configure PC monitor','PC モニターを設定','設定 PC 監控'], ['移除','Remove','削除','移除'],
    ['软件内置界面通过设备 API 传输数据','Built-in UI communicates through device APIs','内蔵 UI はデバイス API で通信します','軟體內建介面透過裝置 API 傳輸資料'], ['刷新数据','Refresh','更新','重新整理資料'], ['配置全息 PC 监控','Configure Holo PC Monitor','Holo PC Monitor を設定','設定全息 PC 監控'], ['等待读取设备状态','Waiting for device status','デバイス状態を待機中','等待读取裝置狀態'], ['应用','Apps','アプリ','應用程式'], ['应用商店','App store','アプリストア','應用程式商店'], ['设备设置','Device settings','デバイス設定','裝置設定'], ['服务设置','Service settings','サービス設定','服務設定'], ['设备应用','Device apps','デバイスアプリ','裝置應用程式'], ['打开应用、退出应用或进入当前应用控制页','Open or exit apps, or open the current app control page','アプリの起動・終了・制御画面を開きます','開啟或退出應用程式，或進入目前應用程式控制頁'], ['退出当前应用','Exit current app','現在のアプリを終了','退出目前應用程式'], ['连接设备后显示应用列表。','Connect a device to view its apps.','デバイス接続後にアプリ一覧を表示します。','連接裝置後顯示應用程式列表。'], ['商店数据由当前设备接口读取','Store data is loaded through the selected device','ストアデータは選択中デバイスから取得します','商店資料由目前裝置介面讀取'], ['刷新应用商店','Refresh app store','ストアを更新','重新整理應用程式商店'], ['尚未读取应用商店。','App store has not been loaded.','アプリストアは未読込です。','尚未讀取應用程式商店。'], ['天气与时区','Weather and time zone','天気とタイムゾーン','天氣與時區'], ['天气地区','Weather location','天気地域','天氣地區'], ['例如 上海市浦东新区','For example Shanghai','例：東京','例如 台北市'], ['界面语言','Interface language','表示言語','介面語言'], ['时区','Time zone','タイムゾーン','時區'], ['亮度与自动息屏','Brightness and auto screen-off','明るさと自動消灯','亮度與自動關屏'], ['屏幕亮度','Screen brightness','画面の明るさ','螢幕亮度'], ['无操作自动息屏','Auto screen-off when idle','無操作時に自動消灯','無操作自動關屏'], ['关闭','Off','オフ','關閉'], ['1 分钟','1 minute','1 分','1 分鐘'], ['5 分钟','5 minutes','5 分','5 分鐘'], ['10 分钟','10 minutes','10 分','10 分鐘'], ['30 分钟','30 minutes','30 分','30 分鐘'], ['1 小时','1 hour','1 時間','1 小時'], ['立即唤醒屏幕','Wake display now','画面を今すぐ点灯','立即喚醒螢幕'], ['息屏与闹钟','Screen-off & alarms','消灯とアラーム','關屏與鬧鐘'], ['定时息屏、亮屏和多组闹钟由设备服务执行。服务页面始终使用当前设备 IP 打开。','Scheduled screen-off, wake, and alarms run on the device. The service page always uses the selected IP.','消灯・点灯スケジュールとアラームはデバイス上で動作し、選択中の IP を使って開きます。','定時關屏、亮屏與多組鬧鐘由裝置服務執行，並始終使用目前裝置 IP 開啟。'], ['进入息屏与闹钟设置','Open screen-off & alarm settings','消灯・アラーム設定を開く','進入關屏與鬧鐘設定'], ['保存到当前设备','Save to selected device','選択中デバイスに保存','保存到目前裝置'], ['设备服务','Device services','デバイスサービス','裝置服務'], ['每个服务设置页都使用当前设备 IP 访问','Every service page uses the selected device IP','各サービス画面は選択中デバイスの IP を使用します','每個服務設定頁都使用目前裝置 IP 存取'], ['连接设备后显示服务。','Connect a device to view services.','デバイス接続後にサービスを表示します。','連接裝置後顯示服務。'], ['进入服务设置','Open service settings','サービス設定を開く','進入服務設定'],
    ['管理设备应用需要的本机后台接口','Manage local APIs required by device apps','デバイスアプリ用ローカル API を管理','管理裝置應用程式所需的本機背景介面'], ['刷新状态','Refresh status','状態を更新','重新整理狀態'], ['内置兼容接口','Built-in compatibility APIs','内蔵互換 API','內建相容介面'], ['随主程序运行，无需单独启动 Python 或 Node。','Runs with the main app; no separate Python or Node process is needed.','メインアプリと同時に動作し、Python や Node は不要です。','隨主程式執行，無需另行啟動 Python 或 Node。'], ['Wi-Fi、设备发现和后台服务状态','Wi-Fi, device discovery, and service status','Wi-Fi、デバイス検出、サービス状態','Wi-Fi、裝置發現與背景服務狀態'], ['清空显示','Clear','クリア','清除顯示'], ['信息','Info','情報','資訊'], ['警告','Warning','警告','警告'], ['错误','Error','エラー','錯誤'], ['等待运行日志…','Waiting for logs…','ログを待機中…','等待執行日誌…'], ['面向 Clocteck Cubic 的 Windows 设备连接、内置控制与上位机服务中心。','Windows connection, built-in control, and companion-service center for Clocteck Cubic.','Clocteck Cubic 用 Windows 接続・内蔵制御・PC サービスセンター。','面向 Clocteck Cubic 的 Windows 裝置連線、內建控制與上位機服務中心。'], ['当前版本','Version','バージョン','目前版本'], ['运行平台','Platform','プラットフォーム','執行平台'], ['设备识别','Device identity','デバイス識別','裝置識別'], ['IPv4 地址','IPv4 address','IPv4 アドレス','IPv4 位址'], ['界面','Interface','UI','介面'], ['本地 WebView2 UI','Local WebView2 UI','ローカル WebView2 UI','本機 WebView2 UI'],
    ['运行中','Running','実行中','執行中'], ['外部运行','External','外部実行','外部執行'], ['已停止','Stopped','停止','已停止'], ['未配置','Not configured','未設定','未設定'], ['监听端口','Listening port','待受ポート','監聽連接埠'], ['当前状态','Status','現在の状態','目前狀態'], ['启动文件','Executable','起動ファイル','啟動檔案'], ['主程序内置','Built in','内蔵','主程式內建'], ['尚未选择','Not selected','未選択','尚未選擇'], ['选择程序','Choose program','プログラムを選択','選擇程式'], ['停止','Stop','停止','停止'], ['启动','Start','開始','啟動'], ['自动运行','Auto start','自動起動','自動執行'], ['随主程序运行','Runs with main app','メインアプリと実行','隨主程式執行'], ['应用控制页','App control page','アプリ制御画面','應用程式控制頁'], ['打开应用','Open app','アプリを開く','開啟應用程式'], ['退出','Exit','終了','退出'], ['服务','Service','サービス','服務'], ['该服务没有控制页','No control page','制御画面なし','此服務沒有控制頁'], ['已安装','Installed','インストール済み','已安裝'], ['更新','Update','更新','更新'], ['安装','Install','インストール','安裝'], ['卸载','Uninstall','アンインストール','解除安裝'],
    ['未知版本','Unknown version','バージョン不明','未知版本'], ['设备没有返回可显示的应用。','The device returned no apps to display.','表示できるアプリがありません。','裝置未回傳可顯示的應用程式。'], ['设备没有返回服务列表。','The device returned no services.','サービス一覧がありません。','裝置未回傳服務列表。'], ['应用商店没有返回应用。','The app store returned no apps.','アプリストアにアプリがありません。','應用程式商店未回傳應用程式。'], ['正在读取应用商店…','Loading app store…','アプリストアを読込中…','正在讀取應用程式商店…'], ['应用商店操作完成','App store operation complete','アプリストア操作完了','應用程式商店操作完成'], ['保存中…','Saving…','保存中…','保存中…'], ['设备设置已保存','Device settings saved','デバイス設定を保存しました','裝置設定已保存'],
    ['设备 ID：{0} · 最近发现：{1}','Device ID: {0} · Last seen: {1}','デバイス ID：{0} · 最終検出：{1}','裝置 ID：{0} · 最近發現：{1}'], ['设备控制 · {0}','Device control · {0}','デバイス制御 · {0}','裝置控制 · {0}'], ['所有数据和页面均来自该 IP，不使用固定域名','All data and pages use this IP; no fixed hostname is used','すべてのデータと画面はこの IP を使用し、固定ホスト名は使いません','所有資料與頁面均來自此 IP，不使用固定網域名稱'], ['已读取 {0} 个应用','Loaded {0} apps','{0} 個のアプリを読み込みました','已讀取 {0} 個應用程式'],
    ['已连接设备','Connected devices','接続中のデバイス','已連接裝置'], ['每个方块显示设备 IP 与当前运行的应用','Each tile shows the device IP and running app','各タイルに IP と実行中アプリを表示','每個方塊顯示裝置 IP 與目前應用程式'], ['刷新设备状态','Refresh devices','デバイス状態を更新','重新整理裝置狀態'], ['正在读取已连接设备…','Loading connected devices…','接続中デバイスを読込中…','正在讀取已連接裝置…'], ['当前没有在线设备，可在“设备”页面扫描或手动连接。','No online devices. Scan or connect manually on the Devices page.','オンラインデバイスがありません。デバイス画面で検索または手動接続してください。','目前沒有在線裝置，請在「裝置」頁面掃描或手動連接。'], ['在线设备','Online device','オンラインデバイス','在線裝置'], ['当前应用：{0}','Current app: {0}','現在のアプリ：{0}','目前應用程式：{0}'],
    ['选择并启动应用','Select and launch an app','アプリを選択して起動','選擇並啟動應用程式'], ['控制页面','Controls','制御画面','控制頁面'], ['应用控制','App controls','アプリ制御','應用程式控制'], ['启动带控制页面的应用后自动显示','Shown automatically after launching an app with controls','制御画面付きアプリの起動後に自動表示','啟動具有控制頁面的應用程式後自動顯示'], ['自动配置 PC 监控','Auto-configure PC monitor','PC モニターを自動設定','自動設定 PC 監控'], ['应用控制页面','App control page','アプリ制御画面','應用程式控制頁面'], ['打开应用后，如果应用提供控制页，将在这里与应用列表左右分屏显示。','If the app provides controls, they appear here beside the app list.','制御画面がある場合、アプリ一覧の横に表示します。','若應用程式提供控制頁面，將在此與應用程式清單左右分割顯示。'],
    ['显示商店图标、应用介绍和安装状态','Shows store icons, descriptions, and install status','ストアアイコン、説明、インストール状態を表示','顯示商店圖示、應用程式介紹與安裝狀態'], ['应用介绍','App description','アプリの説明','應用程式介紹'], ['正在加载完整介绍…','Loading full description…','詳細説明を読込中…','正在載入完整介紹…'], ['选择左侧应用查看完整介绍。','Select an app on the left to view its description.','左のアプリを選択して説明を表示します。','選擇左側應用程式查看完整介紹。'], ['暂无应用介绍','No description available','説明はありません','暫無應用程式介紹'], ['查看介绍','View description','説明を見る','查看介紹'], ['安装状态','Install status','インストール状態','安裝狀態'], ['未安装','Not installed','未インストール','未安裝'], ['发布通道','Release channel','リリースチャンネル','發佈通道'], ['发布时间','Published','公開日時','發佈時間'], ['安装包大小','Package size','パッケージサイズ','安裝包大小'], ['信息来源','Information source','情報源','資訊來源'], ['设备应用商店','Device app store','デバイスアプリストア','裝置應用程式商店'],
    ['全部','All','すべて','全部'], ['可安装','Available','インストール可能','可安裝'], ['启动器','Launcher','ランチャー','啟動器'], ['天气','Weather','天気','天氣'], ['当前应用','Current app','現在のアプリ','目前應用程式'], ['更新中','Updating','更新中','更新中'], ['当前分类没有可显示的应用。','No apps in this category.','このカテゴリに表示できるアプリはありません。','目前分類沒有可顯示的應用程式。'], ['设备已安装，本次目录未返回该应用。','Installed on the device but absent from this catalog.','デバイスにインストール済みですが、このカタログにはありません。','已安裝於裝置，但此目錄未回傳該應用程式。'],
    ['定时息屏与亮屏','Scheduled screen-off & wake','消灯・点灯スケジュール','定時關屏與亮屏'], ['启用定时息屏','Enable schedule','スケジュールを有効化','啟用定時關屏'], ['开启','On','オン','開啟'], ['息屏模式','Screen-off mode','消灯モード','關屏模式'], ['关闭屏幕','Turn display off','画面を消す','關閉螢幕'], ['降低亮度','Dim display','画面を暗くする','降低亮度'], ['息屏时间','Screen-off time','消灯時刻','關屏時間'], ['亮屏时间','Wake time','点灯時刻','亮屏時間'], ['进入设备原生设置页','Open device service page','デバイス標準設定を開く','進入裝置原生設定頁'], ['三组闹钟','Three alarms','3 件のアラーム','三組鬧鐘'], ['试听','Preview','試聴','試聽'], ['闹钟声音','Alarm sound','アラーム音','鬧鐘聲音'], ['默认嘀嘀声','Default beeps','標準ビープ音','預設嗶嗶聲'], ['闹钟 1','Alarm 1','アラーム 1','鬧鐘 1'], ['闹钟 2','Alarm 2','アラーム 2','鬧鐘 2'], ['闹钟 3','Alarm 3','アラーム 3','鬧鐘 3'], ['时间','Time','時刻','時間'], ['重复','Repeat','繰り返し','重複'], ['每日','Daily','毎日','每日'], ['工作日','Weekdays','平日','工作日'], ['周末','Weekend','週末','週末'], ['每周一','Monday','毎週月曜','每週一'], ['每周二','Tuesday','毎週火曜','每週二'], ['每周三','Wednesday','毎週水曜','每週三'], ['每周四','Thursday','毎週木曜','每週四'], ['每周五','Friday','毎週金曜','每週五'], ['每周六','Saturday','毎週土曜','每週六'], ['每周日','Sunday','毎週日曜','每週日'],
    ['服务页面以内嵌方式使用当前设备 IP 访问','Service pages are embedded using the selected device IP','選択中 IP のサービス画面を埋め込み表示','服務頁面以目前裝置 IP 內嵌顯示'], ['选择服务后显示','Shown after selecting a service','サービス選択後に表示','選擇服務後顯示'], ['服务设置页面','Service settings page','サービス設定画面','服務設定頁面'], ['请选择左侧服务。','Select a service on the left.','左のサービスを選択してください。','請選擇左側服務。'],
    ['软件语言','App language','アプリの言語','軟體語言'], ['适用于已完成配网、mDNS 不可用或同一局域网有多台设备的情况。','Use for configured devices, unavailable mDNS, or multiple devices on one LAN.','設定済みデバイス、mDNS が使えない場合、同一 LAN の複数デバイスに使用します。','適用於已完成配網、mDNS 無法使用或同一區域網路有多台裝置。'],
    ['设备已连接','Device connected','デバイス接続済み','裝置已連接'], ['正在连接设备','Connecting to device','デバイスに接続中','正在連接裝置'], ['正在读取设备','Loading device','デバイスを読込中','正在讀取裝置'],
    ['固件更新','Firmware update','ファームウェア更新','韌體更新'], ['等待检查','Not checked','未確認','等待檢查'], ['当前版本','Current version','現在のバージョン','目前版本'], ['最新版本','Latest version','最新バージョン','最新版本'], ['点击“检查更新”获取设备固件更新状态。','Select “Check for updates” to retrieve firmware status.','「更新を確認」でファームウェア状態を取得します。','點擊「檢查更新」取得裝置韌體更新狀態。'], ['检查更新','Check for updates','更新を確認','檢查更新'], ['安装更新','Install update','更新をインストール','安裝更新'], ['检查中','Checking','確認中','檢查中'], ['检查中...','Checking...','確認中...','檢查中...'], ['下载中','Downloading','ダウンロード中','下載中'], ['安装中','Installing','インストール中','安裝中'], ['安装中...','Installing...','インストール中...','安裝中...'], ['即将重启','Rebooting soon','まもなく再起動','即將重新啟動'], ['更新失败','Update failed','更新失敗','更新失敗'], ['有新版本','Update available','更新あり','有新版本'], ['已是最新','Up to date','最新です','已是最新'], ['当前固件无需更新。','Firmware is up to date.','ファームウェアは最新です。','目前韌體無需更新。'], ['发现可安装的新固件。','A new firmware update is available.','新しいファームウェアがあります。','發現可安裝的新韌體。'], ['固件已写入，设备正在重启。','Firmware installed; the device is rebooting.','書き込み完了。デバイスを再起動中です。','韌體已寫入，裝置正在重新啟動。'], ['正在下载 {0}%','Downloading {0}%','ダウンロード中 {0}%','正在下載 {0}%'], ['更新失败：{0}','Update failed: {0}','更新失敗：{0}','更新失敗：{0}'], ['请稍后重试','Try again later','後でもう一度お試しください','請稍後重試'], ['安装固件更新后设备会自动重启。继续安装？','The device will reboot after installation. Continue?','インストール後にデバイスが再起動します。続行しますか？','安裝韌體更新後裝置會自動重新啟動。是否繼續？'], ['已开始安装固件更新','Firmware update started','ファームウェア更新を開始しました','已開始安裝韌體更新'], ['固件更新状态已刷新','Firmware status refreshed','ファームウェア状態を更新しました','韌體更新狀態已重新整理']
  ];

  rows.push(
    ['当前已运行的服务','Running services','実行中のサービス','目前執行中的服務'],
    ['打开设备应用时自动启用所需电脑服务','Required PC services start when a device app opens','デバイスアプリを開くと必要な PC サービスを自動起動します','開啟裝置應用程式時自動啟用所需電腦服務'],
    ['停止服务','Stop service','サービスを停止','停止服務'],
    ['当前没有运行中的电脑服务。打开 Holopet、电脑性能监控等应用后会自动启动对应服务。','No PC service is running. Opening Holopet, PC Monitor, or another supported app starts its service automatically.','実行中の PC サービスはありません。Holopet や PC モニターを開くと対応サービスが自動起動します。','目前沒有執行中的電腦服務。開啟 Holopet、電腦效能監控等應用程式後會自動啟動對應服務。'],
    ['开发工具','Developer tools','開発ツール','開發工具'],
    ['设备开发工具','Device developer tools','デバイス開発ツール','裝置開發工具'],
    ['图片、文件与 Lua 代码管理','Images, files, and Lua code','画像・ファイル・Lua コード管理','圖片、檔案與 Lua 程式碼管理'],
    ['查看软件事件与设备串口实时输出','View software events and live device serial output','ソフトウェアイベントとシリアル出力を表示','查看軟體事件與裝置序列埠即時輸出'],
    ['软件日志','Software logs','ソフトウェアログ','軟體日誌'],
    ['串口输出','Serial output','シリアル出力','序列埠輸出'],
    ['串口','Serial port','シリアルポート','序列埠'],
    ['波特率','Baud rate','ボーレート','鮑率'],
    ['未发现串口','No serial ports','シリアルポートなし','未發現序列埠'],
    ['刷新串口','Refresh ports','ポートを更新','重新整理序列埠'],
    ['连接','Connect','接続','連接'],
    ['断开','Disconnect','切断','中斷連接'],
    ['清空输出','Clear output','出力をクリア','清除輸出'],
    ['串口未连接','Serial disconnected','シリアル未接続','序列埠未連接'],
    ['等待串口输出…','Waiting for serial output…','シリアル出力を待機中…','等待序列埠輸出…'],
    ['请先选择串口','Select a serial port first','シリアルポートを選択してください','請先選擇序列埠'],
    ['通过当前设备 IP 管理文件并编辑 DevRun Lua 代码','Manage files and edit DevRun Lua through the selected device IP','選択中の IP でファイルと DevRun Lua を管理','透過目前裝置 IP 管理檔案並編輯 DevRun Lua'],
    ['图片与文件','Images & files','画像とファイル','圖片與檔案'],
    ['Lua 编辑器','Lua editor','Lua エディター','Lua 編輯器'],
    ['上一级','Up','上へ','上一層'],
    ['刷新','Refresh','更新','重新整理'],
    ['上传文件','Upload files','ファイルをアップロード','上傳檔案'],
    ['尚未读取设备目录','Device folder not loaded','デバイスフォルダー未読込','尚未讀取裝置目錄'],
    ['请选择设备并刷新目录。','Select a device and refresh the folder.','デバイスを選択してフォルダーを更新してください。','請選擇裝置並重新整理目錄。'],
    ['未选择文件','No file selected','ファイル未選択','未選擇檔案'],
    ['从左侧选择图片或文件','Select an image or file on the left','左側から画像またはファイルを選択','從左側選擇圖片或檔案'],
    ['下载','Download','ダウンロード','下載'],
    ['删除','Delete','削除','刪除'],
    ['支持预览 PNG、JPG、GIF、WebP、Lua、JSON 和文本文件。','Preview PNG, JPG, GIF, WebP, Lua, JSON, and text files.','PNG、JPG、GIF、WebP、Lua、JSON、テキストをプレビューできます。','支援預覽 PNG、JPG、GIF、WebP、Lua、JSON 與文字檔案。'],
    ['Lua 代码编辑器','Lua code editor','Lua コードエディター','Lua 程式碼編輯器'],
    ['读取','Load','読込','讀取'],
    ['保存','Save','保存','儲存'],
    ['保存并运行','Save & run','保存して実行','儲存並執行'],
    ['-- 在这里编辑 DevRun Lua 代码','-- Edit DevRun Lua code here','-- DevRun Lua コードを編集','-- 在此編輯 DevRun Lua 程式碼'],
    ['尚未读取','Not loaded','未読込','尚未讀取'],
    ['正在等待应用控制页','Waiting for app controls','アプリ制御画面を待機中','正在等待應用程式控制頁'],
    ['应用启动中','Starting app','アプリ起動中','應用程式啟動中'],
    ['设备完成应用初始化后会自动打开控制页面。','Controls open automatically after the app is ready.','初期化完了後に制御画面を自動表示します。','裝置完成應用程式初始化後會自動開啟控制頁。'],
    ['应用已启动，控制页可稍后刷新重试','App started; refresh shortly to retry controls','アプリ起動済み。少し後で更新してください','應用程式已啟動，可稍後重新整理控制頁'],
    ['应用已启动','App started','アプリを起動しました','應用程式已啟動'],
    ['设备操作已完成','Device action complete','デバイス操作完了','裝置操作已完成'],
    ['控制','Controls','制御','控制'],
    ['打开','Open','開く','開啟'],
    ['无控制页','No controls','制御画面なし','無控制頁'],
    ['{0} 个项目','{0} items','{0} 件','{0} 個項目'],
    ['目录已刷新','Folder refreshed','フォルダーを更新しました','目錄已重新整理'],
    ['当前目录为空。','This folder is empty.','このフォルダーは空です。','目前目錄為空。'],
    ['目录','Folder','フォルダー','目錄'],
    ['预览','Preview','プレビュー','預覽'],
    ['正在读取预览…','Loading preview…','プレビュー読込中…','正在讀取預覽…'],
    ['该文件不支持内置预览，可下载到电脑查看。','No built-in preview; download the file to view it.','内蔵プレビュー非対応です。ダウンロードしてください。','不支援內建預覽，可下載到電腦查看。'],
    ['行','lines','行','行'],
    ['字符','characters','文字','字元'],
    ['未修改','Unmodified','未変更','未修改'],
    ['已修改','Modified','変更あり','已修改'],
    ['Lua 代码已读取','Lua code loaded','Lua コードを読み込みました','Lua 程式碼已讀取'],
    ['已保存并运行 DevRun','Saved and started DevRun','保存して DevRun を起動しました','已儲存並執行 DevRun'],
    ['Lua 代码已保存','Lua code saved','Lua コードを保存しました','Lua 程式碼已儲存'],
    ['确认删除 {0}？','Delete {0}?','{0} を削除しますか？','確認刪除 {0}？'],
    ['正在读取设备文件','Loading device files','デバイスファイルを読込中','正在讀取裝置檔案'],
    ['正在上传 {0}','Uploading {0}','{0} をアップロード中','正在上傳 {0}'],
    ['已上传 {0} 个文件','Uploaded {0} files','{0} ファイルをアップロードしました','已上傳 {0} 個檔案'],
    ['正在下载设备文件','Downloading device file','デバイスファイルをダウンロード中','正在下載裝置檔案'],
    ['文件已保存到电脑','File saved to the PC','PC に保存しました','檔案已儲存到電腦'],
    ['设备文件已删除','Device file deleted','デバイスファイルを削除しました','裝置檔案已刪除']
    ,['文件管理','File manager','ファイル管理','檔案管理']
    ,['设备文件管理','Device file manager','デバイスファイル管理','裝置檔案管理']
    ,['管理图片、GIF、音乐、歌词和应用文件','Manage images, GIFs, music, lyrics, and app files','画像、GIF、音楽、歌詞、アプリファイルを管理','管理圖片、GIF、音樂、歌詞與應用程式檔案']
    ,['请选择设备','Select a device','デバイスを選択','請選擇裝置']
    ,['管理设备图片、GIF、音乐、歌词和应用文件','Manage device images, GIFs, music, lyrics, and app files','画像、GIF、音楽、歌詞、アプリファイルを管理','管理裝置圖片、GIF、音樂、歌詞與應用程式檔案']
    ,['编辑并运行 DevRun Lua 代码','Edit and run DevRun Lua code','DevRun Lua コードを編集・実行','編輯並執行 DevRun Lua 程式碼']
    ,['通过当前设备 IP 编辑并运行 DevRun Lua 代码','Edit and run DevRun Lua through the selected device IP','選択中の IP で DevRun Lua を編集・実行','透過目前裝置 IP 編輯並執行 DevRun Lua']
    ,['媒体目录','Media folders','メディアフォルダー','媒體目錄']
    ,['图片','Images','画像','圖片']
    ,['/sd/images · 相册','/sd/images · Photos','/sd/images · 写真','/sd/images · 相簿']
    ,['/sd/gifs · 动图播放器','/sd/gifs · GIF player','/sd/gifs · GIF プレーヤー','/sd/gifs · 動圖播放器']
    ,['音乐与歌词','Music & lyrics','音楽と歌詞','音樂與歌詞']
    ,['应用文件','App files','アプリファイル','應用程式檔案']
    ,['/sd/apps · App 数据','/sd/apps · App data','/sd/apps · アプリデータ','/sd/apps · App 資料']
    ,['上传图片与 GIF','Upload images & GIFs','画像と GIF をアップロード','上傳圖片與 GIF']
    ,['自动处理全部动画帧；音乐、歌词和其他文件保持原样。','Processes every animation frame; music, lyrics, and other files stay unchanged.','全アニメーションフレームを処理し、音楽・歌詞・その他のファイルは変更しません。','自動處理所有動畫影格；音樂、歌詞與其他檔案保持原樣。']
    ,['320×240 处理方式','320×240 processing','320×240 処理方法','320×240 處理方式']
    ,['居中裁切并填满','Center crop to fill','中央で切り抜いて全面表示','置中裁切並填滿']
    ,['等比例适应并补黑边','Fit with black bars','縦横比を維持して黒帯を追加','等比例適應並補黑邊']
    ,['保持原始尺寸','Keep original size','元のサイズを維持','保持原始尺寸']
    ,['上传到此目录','Upload here','ここにアップロード','上傳到此目錄']
    ,['支持预览 PNG、JPG、GIF、WebP、Lua、JSON、歌词和文本文件。','Preview PNG, JPG, GIF, WebP, Lua, JSON, lyrics, and text files.','PNG、JPG、GIF、WebP、Lua、JSON、歌詞、テキストをプレビューできます。','支援預覽 PNG、JPG、GIF、WebP、Lua、JSON、歌詞與文字檔案。']
    ,['正在处理 {0} 为 320×240','Processing {0} at 320×240','{0} を 320×240 に処理中','正在將 {0} 處理為 320×240']
    ,['已上传 {0} 个文件，图片已处理为 320×240','Uploaded {0} files; images processed at 320×240','{0} ファイルをアップロードし、画像を 320×240 に処理しました','已上傳 {0} 個檔案，圖片已處理為 320×240']
    ,['串口工具','Serial tool','シリアルツール','序列埠工具']
    ,['连接设备串口并实时读取输出信息','Connect to a device serial port and read live output','デバイスのシリアルポートに接続して出力をリアルタイム表示','連接裝置序列埠並即時讀取輸出資訊']
    ,['当前串口','Current port','現在のポート','目前序列埠']
    ,['已接收','Received','受信済み','已接收']
    ,['连接时间','Connected at','接続時刻','連接時間']
    ,['已连接','Connected','接続済み','已連接']
    ,['串口错误','Serial error','シリアルエラー','序列埠錯誤']
    ,['查看软件运行事件和错误信息','View application events and errors','アプリのイベントとエラーを表示','查看軟體執行事件與錯誤資訊']
    ,['当前网络','Current network','現在のネットワーク','目前網路']
    ,['网络','Network','ネットワーク','網路']
    ,['正在获取 IP','Getting IP','IP を取得中','正在取得 IP']
    ,['请检查电脑网络','Check the PC network','PC ネットワークを確認してください','請檢查電腦網路']
    ,['该应用无控制页','This app has no controls','このアプリには制御画面がありません','此應用程式無控制頁']
    ,['应用正在设备上运行，但没有提供 Web 控制页面。','The app is running on the device but does not provide a Web control page.','アプリはデバイス上で実行中ですが、Web 制御画面はありません。','應用程式正在裝置上執行，但未提供 Web 控制頁面。']
    ,['打开应用后，如果应用提供控制页，将在这里与应用列表左右分屏显示。','If the app provides controls, they appear here beside the app list.','制御画面がある場合、アプリ一覧の横に表示します。','若應用程式提供控制頁，會在此與應用程式列表分割顯示。']
    ,['从左侧选择图片、文件或文件夹','Select an image, file, or folder on the left','左側から画像、ファイル、フォルダーを選択','從左側選擇圖片、檔案或資料夾']
    ,['新建文件夹','New folder','新しいフォルダー','新增資料夾']
    ,['粘贴','Paste','貼り付け','貼上']
    ,['重命名','Rename','名前を変更','重新命名']
    ,['复制','Copy','コピー','複製']
    ,['剪切','Cut','切り取り','剪下']
    ,['在 DevRun 中编辑','Edit in DevRun','DevRun で編集','在 DevRun 中編輯']
    ,['文件夹已选择，双击左侧项目可打开。','Folder selected. Double-click it on the left to open.','フォルダーを選択しました。左側でダブルクリックして開きます。','已選擇資料夾，雙擊左側項目即可開啟。']
    ,['请输入新文件夹名称','Enter a new folder name','新しいフォルダー名を入力','請輸入新資料夾名稱']
    ,['请输入新名称','Enter a new name','新しい名前を入力','請輸入新名稱']
    ,['名称不能包含斜杠','Names cannot contain slashes','名前にスラッシュは使用できません','名稱不能包含斜線']
    ,['已复制 {0}，请选择目标文件夹后粘贴','Copied {0}. Open a destination folder and paste.','{0} をコピーしました。貼り付け先を開いてください。','已複製 {0}，請選擇目標資料夾後貼上']
    ,['已剪切 {0}，请选择目标文件夹后粘贴','Cut {0}. Open a destination folder and paste.','{0} を切り取りました。貼り付け先を開いてください。','已剪下 {0}，請選擇目標資料夾後貼上']
    ,['源文件已经在当前目录中','The source is already in this folder','元の項目は既にこのフォルダーにあります','來源項目已在目前資料夾中']
    ,['剪贴板项目来自另一台设备','The clipboard item belongs to another device','クリップボードの項目は別のデバイスにあります','剪貼簿項目來自另一台裝置']
    ,['已从 {0} 导入，尚未保存','Imported from {0}; not saved yet','{0} から読み込みました。未保存です。','已從 {0} 匯入，尚未儲存']
    ,['正在重命名设备项目','Renaming device item','デバイス項目の名前を変更中','正在重新命名裝置項目']
    ,['设备项目已重命名','Device item renamed','デバイス項目の名前を変更しました','裝置項目已重新命名']
    ,['正在创建设备文件夹','Creating device folder','デバイスフォルダーを作成中','正在建立裝置資料夾']
    ,['设备文件夹已创建','Device folder created','デバイスフォルダーを作成しました','裝置資料夾已建立']
    ,['正在移动设备项目','Moving device item','デバイス項目を移動中','正在移動裝置項目']
    ,['正在复制设备项目','Copying device item','デバイス項目をコピー中','正在複製裝置項目']
    ,['设备项目已移动','Device item moved','デバイス項目を移動しました','裝置項目已移動']
    ,['设备项目已复制','Device item copied','デバイス項目をコピーしました','裝置項目已複製']
    ,['从服务器读取应用信息并选择设备或 PC 下载','Load app information from the server and choose device or PC download','サーバーからアプリ情報を取得し、デバイスまたは PC ダウンロードを選択','從伺服器讀取應用資訊並選擇裝置或 PC 下載']
    ,['安装方式','Install method','インストール方法','安裝方式']
    ,['1. 设备下载','1. Device download','1. デバイスでダウンロード','1. 裝置下載']
    ,['2. PC 下载','2. PC download','2. PC でダウンロード','2. PC 下載']
    ,['PC 下载需要电脑能够正常访问 GitHub；GitHub 版本与商店版本不一致时会停止安装。','PC download requires GitHub access. Installation stops if the GitHub and store versions differ.','PC ダウンロードには GitHub への接続が必要です。GitHub とストアのバージョンが異なる場合は停止します。','PC 下載需要電腦能正常存取 GitHub；GitHub 與商店版本不一致時會停止安裝。']
    ,['电脑正在读取应用商店','The PC is loading the app store','PC でアプリストアを読込中','電腦正在讀取應用程式商店']
    ,['下载到电脑','Download to PC','PC にダウンロード','下載到電腦']
    ,['安装到设备','Install to device','デバイスにインストール','安裝到裝置']
    ,['下载失败','Download failed','ダウンロード失敗','下載失敗']
    ,['安装失败','Installation failed','インストール失敗','安裝失敗']
    ,['已下载到电脑','Downloaded to PC','PC にダウンロード済み','已下載到電腦']
    ,['正在刷新 Launcher 应用列表','Refreshing Launcher app list','Launcher のアプリ一覧を更新中','正在重新整理 Launcher 應用程式列表']
    ,['传输接口','Transfer interface','転送インターフェース','傳輸介面']
    ,['固件 FS（推荐）','Firmware FS (recommended)','ファームウェア FS（推奨）','韌體 FS（建議）']
    ,['DevTools 分块','DevTools chunks','DevTools 分割転送','DevTools 分塊']
  );

  const indexes = { 'zh-CN':0, en:1, ja:2, 'zh-TW':3 };
  const map = new Map(rows.map(row => [row[0], row]));
  const textSources = new WeakMap();
  const attrSources = new WeakMap();
  const requestedLanguage = new URLSearchParams(location.search).get('lang');
  let language = normalize(requestedLanguage || localStorage.getItem('cubic.center.language') || 'zh-CN');

  function normalize(value) {
    const raw = String(value || '').trim();
    if (indexes[raw] !== undefined) return raw;
    if (/^en/i.test(raw)) return 'en';
    if (/^ja/i.test(raw)) return 'ja';
    if (/^zh[-_](TW|HK|Hant)/i.test(raw)) return 'zh-TW';
    return 'zh-CN';
  }

  function t(source) {
    const row = map.get(String(source));
    return row ? row[indexes[language]] : String(source);
  }

  function format(source, ...values) {
    return values.reduce((text, value, index) => text.replaceAll(`{${index}}`, String(value)), t(source));
  }

  function translateTextNode(node) {
    const raw = textSources.get(node) ?? node.nodeValue;
    if (!textSources.has(node)) textSources.set(node, raw);
    const match = raw.match(/^(\s*)(.*?)(\s*)$/s);
    if (!match || !match[2]) return;
    node.nodeValue = match[1] + t(match[2]) + match[3];
  }

  function localize(root = document.body) {
    if (!root) return;
    if (root.nodeType === Node.TEXT_NODE) translateTextNode(root);
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) translateTextNode(walker.currentNode);
    const elements = root.nodeType === Node.ELEMENT_NODE ? [root, ...root.querySelectorAll('*')] : [...root.querySelectorAll('*')];
    elements.forEach(element => ['placeholder','title','aria-label'].forEach(name => {
      if (!element.hasAttribute(name)) return;
      let sources = attrSources.get(element);
      if (!sources) { sources = {}; attrSources.set(element, sources); }
      if (!(name in sources)) sources[name] = element.getAttribute(name);
      element.setAttribute(name, t(sources[name]));
    }));
  }

  function apply(value, persist = true) {
    language = normalize(value);
    document.documentElement.lang = language;
    document.title = language === 'zh-CN' ? 'Clocteck Cubic Center' : 'Clocteck Cubic Center';
    if (persist) localStorage.setItem('cubic.center.language', language);
    localize(document.body);
    return language;
  }

  window.CubicI18n = { apply, localize, t, format, normalize, get language() { return language; } };
})();
