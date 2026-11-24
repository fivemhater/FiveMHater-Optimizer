# FiveMHater Optimizer v2.3 — Derleme, İkon ve Dijital İmzalama Rehberi

Teknoloji: **C# .NET 8 · WPF · tek dosya win-x64 .exe** (packer / obfuscator / UPX / sıkıştırma **YOK**).
Yönetici yetkisi `app.manifest` içindeki `requireAdministrator` ile istenir.

---

## 1. Tam klasör ağacı

```
FiveMHaterOptimizer-source/
├── FiveMHaterOptimizer.sln
├── DERLEME_VE_IMZALAMA.md
├── .gitignore
├── tools/
│   ├── make_assets.sh          # PNG → şeffaf logo + çok katmanlı ICO (ImageMagick)
│   ├── publish.ps1             # Windows'ta tek dosya derleme
│   └── publish.sh              # Linux/macOS'ta çapraz derleme
└── FiveMHaterOptimizer/
    ├── FiveMHaterOptimizer.csproj   # AssemblyInfo: Product/Company/Copyright/Version 2.3.0.0/ApplicationIcon
    ├── app.manifest                 # requireAdministrator, Win10/11 GUID, PerMonitorV2 DPI, longPathAware
    ├── App.xaml / App.xaml.cs       # Tema, stiller, global hata yakalama
    ├── Assets/
    │   ├── fivemhater_logo.png      # Şeffaf arka planlı logo (WPF Resource olarak gömülü)
    │   └── fivemhater.ico           # 16/24/32/48/64/128/256 katmanlı ikon
    ├── Converters/Converters.cs
    ├── Models/
    │   ├── BackupModels.cs          # backup.json şeması
    │   ├── BenchmarkResult.cs
    │   ├── OptimizationOptions.cs   # Ultra / Extreme / Balanced / Sadece Ağ & Ping + anahtarlar
    │   └── SystemInfo.cs
    ├── Services/
    │   ├── AppPaths.cs  Logger.cs  NativeMethods.cs  PrivilegeHelper.cs  ProcessRunner.cs
    │   ├── RegistryUtil.cs  RegistryTweaker.cs  BackupService.cs  VerificationList.cs  WmiUtil.cs
    │   ├── HardwareDetector.cs  BenchmarkService.cs  ServiceTweaker.cs  ScheduledTaskTweaker.cs
    │   ├── TcpGlobalService.cs  DnsService.cs  PowerService.cs  BcdService.cs  FileCleaner.cs
    │   ├── OptimizationContext.cs  OptimizerEngine.cs  RestoreEngine.cs   # Modül 10: Tam Geri Al
    │   └── Modules/
    │       ├── IOptimizationModule.cs
    │       ├── SystemPrepModule.cs       # Modül 1  Sistem hazırlığı + yedek + geri yükleme noktası
    │       ├── ServiceProcessModule.cs   # Modül 2  Servis & işlem azaltma
    │       ├── UltraDebloatModule.cs     # Modül 2b Ultra: başlangıç uygulamaları + derin servis/görev azaltma
    │       ├── BloatwareModule.cs        # Modül 2c Opsiyonel: hazır uygulamaları kaldırma (WinRT PackageManager, geri alınamaz)
    │       ├── NetworkModule.cs          # Modül 3  Ağ ayarları (TCP/netsh/NIC/QoS)
    │       ├── DnsModule.cs              # Modül 3  DNS seçimi
    │       ├── PowerCpuModule.cs         # Modül 4  Güç planı & CPU
    │       ├── GpuDisplayModule.cs       # Modül 5  GPU & Görüntü
    │       ├── InputLatencyModule.cs     # Modül 6  Girdi gecikmesi
    │       ├── GameSettingsModule.cs     # Modül 7  Oyun ayarları (exe + settings.xml)
    │       ├── FiveMCacheModule.cs       # Modül 7  FiveM önbelleği
    │       ├── RamDiskModule.cs          # Modül 8  RAM & Disk
    │       ├── DismCleanupModule.cs      # Modül 8b Opsiyonel: DISM /StartComponentCleanup
    │       └── VerificationModule.cs     # Modül 9  Doğrulama ve sonuç
    ├── ViewModels/  MainViewModel.cs  ObservableObject.cs  RelayCommand.cs
    └── Views/       MainWindow.xaml(.cs)  LogoView.xaml(.cs)   # Vektörel yedek logo dahil
```

Çalışma zamanı dosyaları:

| Dosya | Açıklama |
|---|---|
| `%ProgramData%\FiveMHater\backup.json` | İlk orijinal yedek. Sonraki çalıştırmalar yalnızca **yeni** öğe ekler, eskileri **asla ezmez**. |
| `%ProgramData%\FiveMHater\logs\optimizer_yyyyMMdd_HHmmss.log` | Kalıcı optimizasyon logu |
| `%ProgramData%\FiveMHater\logs\restore_yyyyMMdd_HHmmss.log` | Geri alma logu |
| `%ProgramData%\FiveMHater\backup_restored_*.json` | Başarılı geri almadan sonra arşivlenen yedek |
| `...\settings.xml.fivemhater.bak` / `gta5_settings.xml.fivemhater.bak` | GTA V / FiveM grafik ayarı yedeği |

---

## 2. Logoyu 16/32/48/256 katmanlı profesyonel `.ico` dosyasına çevirme

### Windows (ImageMagick 7 — `winget install ImageMagick.ImageMagick`)

```powershell
cd FiveMHaterOptimizer\Assets
# (İsteğe bağlı) siyah zemini şeffaf yap: alfa = en parlak kanal, kenarları kırp, 512x512 kare
magick kaynak_logo.png -alpha off ( +clone -separate -evaluate-sequence max -level 3%,60% ) `
  -compose CopyOpacity -composite -trim +repage -resize 480x480 -background none -gravity center -extent 512x512 fivemhater_logo.png

# Çok katmanlı ICO (256 katman PNG sıkıştırmalı, küçükler BMP)
magick fivemhater_logo.png -background none -resize 256x256 -gravity center -extent 256x256 `
  -define icon:auto-resize=256,128,64,48,32,24,16 fivemhater.ico

# Kontrol
magick identify fivemhater.ico
```

### Linux / WSL
```bash
tools/make_assets.sh /yol/kaynak_logo.gif   # GIF'in ilk karesini alır, PNG + ICO üretir
```

Kurallar: kare kaynak (≥512 px) kullanın; 16 ve 32 px katmanlarında ince detaylar kaybolur, bu yüzden “FH” harfleri kalın olmalı.
Alternatif (GUI): GIMP → Dışa Aktar → `.ico` → her katman için 16/32/48/256 seçin.

---

## 3. Tek dosya `.exe` derleme

Gereksinim: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
cd FiveMHaterOptimizer
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none
```

Çıktı: `FiveMHaterOptimizer\bin\Release\net8.0-windows\win-x64\publish\FiveMHaterOptimizer.exe` (~140 MB, .NET runtime + WPF içinde, kurulum gerektirmez).

- `EnableCompressionInSingleFile` bilerek **kapalı**: sıkıştırılmış/yüksek entropili paketler antivirüs sezgisellerini tetikleyebilir.
- `PublishTrimmed` kapalı: WPF ve WMI yansıma kullanır, kırpma çalışma zamanı hatası doğurur.
- Linux/macOS'ta da derlenir (`EnableWindowsTargeting=true` csproj'de tanımlı): `tools/publish.sh`.
- Native WPF kütüphaneleri ilk açılışta `%TEMP%\.net\FiveMHaterOptimizer\...` altına çıkarılır (uygulama bu klasörü temizlikte korur).

---

## 4. SmartScreen ve Defender uyarısı almamak için gerçek imzalama

### 4.1 Seçenekler

| Yöntem | Kimler için | Not |
|---|---|---|
| **Azure Artifact Signing** (eski adı *Azure Trusted Signing*) | ABD, Kanada, AB, İngiltere'deki kuruluşlar (bireysel geliştirici için de bölge kısıtı var) | ~10 $/ay, sertifika Microsoft tarafından yönetilir, itibar kimliğinize bağlanır. **Türkiye'den bireysel başvuru şu an desteklenmiyor olabilir — bu durumda OV sertifika kullanın.** |
| **OV Code Signing** (Sectigo, DigiCert, Certum, GlobalSign, SSL.com…) | Bireysel/şirket, her ülke | 2023'ten beri özel anahtar donanım (USB token / bulut HSM) üzerinde tutulmak zorunda. |
| **EV Code Signing** | Tescilli şirket | Ağustos 2024'ten itibaren EV artık SmartScreen'i *anında* geçmez; itibar OV gibi indirme sayısıyla birikir. Kernel sürücü imzası için gereklidir (bu uygulamada gerek yok). |

### 4.2 Azure Artifact Signing ile imzalama

```powershell
winget install -e --id Microsoft.Azure.ArtifactSigningClientTools
az login       # veya CI için AZURE_CLIENT_ID / AZURE_TENANT_ID / AZURE_CLIENT_SECRET ortam değişkenleri
```

`metadata.json`:
```json
{
  "Endpoint": "https://weu.codesigning.azure.net/",
  "CodeSigningAccountName": "<hesap-adı>",
  "CertificateProfileName": "<sertifika-profili>"
}
```
(Endpoint, hesabın oluşturulduğu bölgeyle aynı olmalı; uyuşmazlık 403 hatası verir. Hesaba “Trusted Signing Certificate Profile Signer” rolü atanmalı.)

```powershell
signtool sign /v /fd SHA256 /tr "http://timestamp.acs.microsoft.com" /td SHA256 `
  /dlib "C:\Program Files (x86)\Microsoft\ArtifactSigningClientTools\bin\Azure.CodeSigning.Dlib.dll" `
  /dmdf .\metadata.json FiveMHaterOptimizer.exe
```

### 4.3 OV / EV sertifika (USB token veya .pfx) ile imzalama

`signtool.exe`, Windows SDK ile gelir: `C:\Program Files (x86)\Windows Kits\10\bin\<sürüm>\x64\signtool.exe`.

```powershell
# Token / sertifika deposundaki sertifika ile (otomatik seçim)
signtool.exe sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a FiveMHaterOptimizer.exe

# Belirli sertifika (parmak izi ile)
signtool.exe sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /sha1 <SERTIFIKA_PARMAK_IZI> FiveMHaterOptimizer.exe

# .pfx dosyası ile (yalnızca eski tip/test sertifikaları için)
signtool.exe sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f sertifika.pfx /p <PAROLA> FiveMHaterOptimizer.exe
```

**Doğrulama:**
```powershell
signtool.exe verify /pa /v FiveMHaterOptimizer.exe
Get-AuthenticodeSignature .\FiveMHaterOptimizer.exe | Format-List
```
`Successfully verified` ve `Status : Valid` görmelisiniz. Zaman damgası (`/tr`) sayesinde sertifika süresi dolsa bile imza geçerli kalır.

Önemli sıra: **önce `dotnet publish`, sonra imza.** İmzadan sonra exe'yi değiştirmeyin (yeniden paketleme, kaynak düzenleme imzayı bozar).

### 4.4 Kendi imzanla (self-signed) test etmek — ve neden dağıtım için YETERSİZ olduğu

Sadece kendi bilgisayarınızda imza akışını test etmek için:

```powershell
# Yönetici PowerShell
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=FiveMHater Test" `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(2)

# Yalnızca BU bilgisayarda güvenilir yap (test amaçlı!)
Export-Certificate -Cert $cert -FilePath .\FiveMHaterTest.cer
Import-Certificate -FilePath .\FiveMHaterTest.cer -CertStoreLocation Cert:\CurrentUser\Root
Import-Certificate -FilePath .\FiveMHaterTest.cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher

signtool.exe sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /sha1 $cert.Thumbprint FiveMHaterOptimizer.exe
signtool.exe verify /pa /v FiveMHaterOptimizer.exe
```

Farkı net olarak:

- **Self-signed:** Sertifikayı siz ürettiniz, hiçbir kök sertifika otoritesi (CA) sizi doğrulamadı. Yalnızca sertifikayı elle “Güvenilen Kök” deposuna eklediğiniz bilgisayarda geçerli görünür. Diğer herkeste imza “güvenilmeyen kök” olarak görünür, SmartScreen **“Windows bilgisayarınızı korudu – Bilinmeyen yayımcı”** der ve Defender'a hiçbir itibar sağlamaz. Kullanıcılara bu sertifikayı kök depoya eklemelerini **asla** söylemeyin (güvenlik açığıdır).
- **Gerçek (OV/EV/Artifact Signing):** Kimliğiniz bir CA veya Microsoft tarafından doğrulanır, sertifika zinciri Windows'un yerleşik köklerine bağlanır. Her bilgisayarda “Doğrulanmış yayımcı: FiveMHater” görünür. SmartScreen itibarı imzalayan kimliğe bağlanır ve indirme sayısı arttıkça uyarı kaybolur (ilk günlerde uyarı yine görülebilir — bu normaldir).

### 4.5 Microsoft Security Intelligence (WDSI) — false-positive / temiz dosya bildirimi

Defender veya SmartScreen exe'yi yanlışlıkla işaretlerse:

1. İmzalı exe'nin SHA-256'sını alın: `Get-FileHash .\FiveMHaterOptimizer.exe -Algorithm SHA256`
2. <https://www.microsoft.com/en-us/wdsi/filesubmission> adresine gidin, **“Software developer”** seçin ve Microsoft hesabıyla giriş yapın.
3. Ürün: **Microsoft Defender Antivirus (Windows 10/11)** (SmartScreen uyarısı için “Microsoft Defender SmartScreen” seçeneği).
4. Dosyayı yükleyin (ZIP içine koyup parola `infected` vermek de kabul edilir), tespit adını (ör. `Trojan:Win32/Wacatac.B!ml`) ve şu açıklamayı yazın:
   *“Bu, FiveMHater tarafından geliştirilen, imzalı ve açık kaynak kodlu bir sistem optimizasyon aracıdır. Packer/obfuscator içermez; yalnızca Microsoft.Win32.Registry, WMI ve powercfg/netsh/bcdedit/schtasks gibi resmi Windows araçlarını açık argümanlarla kullanır; tüm değişiklikler yedeklenir ve geri alınabilir.”*
5. **“Incorrectly detected as malware/malicious”** seçin ve gönderin. Genelde 1–3 iş günü içinde sonuç e-postayla gelir; temiz bulunursa tanım güncellemesiyle yanlış tespit kaldırılır.
6. Kullanıcı tarafında hemen düzelmesi için: `MpCmdRun.exe -RemoveDefinitions -DynamicSignatures` ardından `MpCmdRun.exe -SignatureUpdate`.

**İtibarı hızlandırmak için öneriler**
- Her sürümü aynı sertifika/kimlikle imzalayın; sürümler arasında sertifika değiştirmeyin.
- Dosya adını ve `Product/Company` meta verisini sabit tutun (csproj'de tanımlı).
- İndirmeyi HTTPS üzerinden, kendi alan adınızdan dağıtın; exe'yi şifreli ZIP veya self-extracting paket içinde dağıtmayın.
- Kaynak kodu (bu ZIP) yayınlamak, inceleme taleplerinde güven sağlar.

---

## 5. Güvenlik ilkeleri (uygulamada uygulanan)

- Python/PyInstaller, AutoIt, Batch-to-EXE, UPX, packer, obfuscator **yok**.
- `powershell.exe` hiç çağrılmaz (ne `-EncodedCommand` ne `-WindowStyle Hidden`).
- Windows Defender'a dışlama (exclusion) **eklenmez**; Defender, Güvenlik Duvarı, Windows Update servislerine dokunulmaz.
- Başlangıçta web sitesi/Discord açma, reklam, başlangıca ekleme **yok**.
- Sistem dosyası silinmez; TrustedInstaller izinleri kırılmaz; pagefile kapatılmaz; DC (pil) güç değerlerine dokunulmaz.
- Çalıştırılan araçlar yalnızca System32'deki resmi araçlardır: `powercfg.exe`, `netsh.exe`, `fsutil.exe`, `ipconfig.exe`, `bcdedit.exe`, `schtasks.exe`, `gpupdate.exe` (QoS ilkesi yenileme), `shutdown.exe` (yalnızca kullanıcı “Yeniden Başlat”a basıp onaylarsa). Hepsi `CreateNoWindow = true`, açık argümanlarla çalışır ve çıktıları loga yazılır.

## v2.1 — Ultra / Full+Full profili

- Yeni 4. profil **Ultra / Full+Full** (önerilen). Extreme'in tamamını + aşağıdakileri uygular:
  - Başlangıç uygulamaları (HKCU/HKLM Run, Run32, Başlangıç klasörleri, paketli uygulama başlangıç görevleri) koruma listesi dışındaysa
    `Explorer\StartupApproved` ile kapatılır (Görev Yöneticisi > Başlangıç ile aynı yöntem; dosya/kayıt silinmez). RunOnce yalnızca loglanır.
  - Üçüncü taraf güncelleyici görevleri (Edge/Google/Adobe/CCleaner/Opera/Brave/Dropbox/NVIDIA telemetri) kapatılır.
  - Ek servisler El ile/Devre dışı (CDPSvc, OneSyncSvc, MessagingService, PimIndexMaintenanceSvc, BcastDVRUserService, Xbox servisleri,
    WMPNetworkSvc, icssvc, SSDP/UPnP, akıllı kart, TapiSrv, PcaSvc, TrkWks, DPS, güncelleyici servisleri, VM değilse vmic*,
    hipervizör yoksa HvHost, biyometrik cihaz yoksa WbioSrvc, seçilirse Bluetooth).
  - Ek zamanlanmış görevler (Office telemetri, Siuf, Haritalar, WER kuyruğu, Autochk, DiskFootprint, Bellek tanılama, Güç tanılama, Xbl).
  - Widgets/Copilot/Teams Sohbet/Arama vurguları/Cortana/Phone Link arka planı/Edge ön yükleme/Teslim Optimizasyonu P2P kapatılır.
- Asla dokunulmaz: WpnService (bildirimler), TabletInputService/TextInputManagementService, UnistoreSvc, UserDataSvc, AppXSvc,
  StateRepository, DoSvc, InstallService, wlidsvc, ses/ağ/GPU/Defender/Windows Update servisleri, ScheduledDefrag (TRIM).
- Ultra anahtarları: "Discord/Steam başlangıçta açılsın", "OneDrive başlangıçta açılmasın", "Bluetooth kullanmıyorum".
- Donanıma/CPU modeline özel ayar yoktur. Tüm değişiklikler `backup.json`'a yazılır ve **Tam Geri Al** ile geri alınır.
- İşlem sayısını yeniden başlattıktan 2-3 dakika sonra ölçün.

## v2.2 — Güvenle eklenebilen eksikler

- **Hedef çerçeve:** `net8.0-windows10.0.19041.0` (WinRT `PackageManager` API için; minimum Windows 10 1809). PowerShell kullanılmaz.
- **Ultra servisleri → Devre dışı:** TermService, SessionEnv, UmRdpService (Uzak Masaüstü), RemoteAccess, NcbService, SCPolicySvc,
  AssignedAccessManagerSvc, NaturalAuthentication. Sistemde olmayanlar atlanır; başlangıç tipleri yedeklenir.
- **Ultra görevleri:** RegIdleBackup, RemoteAssistanceTask, Ras\MobilityManager, MNO Metadata Parser, UPnPHostConfig,
  WDI\ResolutionHost, Wininet\CacheTask, SettingSync\BackgroundUploadTask, SettingSync\NetworkStateChangeTask.
- **Gizlilik (Extreme + Ultra):** AllowTelemetry=0 (Home/Pro'da fiilen 1), reklam kimliği kapalı, Start_TrackProgs=0, Wi-Fi Sense
  (AutoConnectAllowedOEM=0), geri bildirim sıklığı 0, Etkinlik Geçmişi ilkeleri 0, MenuShowDelay="0".
- **Güç (Extreme + Ultra):** FiveMHater planında AC Uyku (STANDBYIDLE) ve AC Disk kapatma (DISKIDLE) = 0; orijinal AC değerleri
  `PowerSettings` altında yedeklenir. Masaüstünde `powercfg /hibernate off` (önceki `HibernateEnabled` yedeklenir; Geri Al açıksa
  `/hibernate on` çalıştırır). Dizüstünde hazırda bekletmeye ve DC değerlerine dokunulmaz.
- **Temizlik:** `%LOCALAPPDATA%\Microsoft\Windows\INetCache` (kilitli dosyalar atlanır). Ultra'da sistem diski
  `MSFT_PhysicalDisk.MediaType = 4` (SSD) ise `defrag.exe C: /L` (resmi retrim); bilinmiyorsa atlanır.
- **Opsiyonel (varsayılan KAPALI):** "Hazır gelen gereksiz uygulamaları kaldır (geri alınamaz)". Yalnızca mevcut kullanıcı; provisioned
  paketlere dokunulmaz; Xbox/Store/Kamera/Hesap Makinesi/Fotoğraflar vb. korunur. Kaldırılanlar `RemovedAppx` altında listelenir,
  Tam Geri Al yalnızca bunları loglar (Microsoft Store'dan yeniden yüklenebilir).

## v2.3 — Ek güvenli ayarlar, arayüz düzeltmeleri, Discord bağlantısı

- **Disk:** `fsutil behavior set DisableDeleteNotify 0` (TRIM bildirimleri açık). Önce `FileSystem\DisableDeleteNotification` yedeklenir; Geri Al orijinali yazar.
- **MMCSS Tasks\Games:** `Latency Sensitive="True"`, `Affinity=0` (mevcut `Background Only="False"`, `Clock Rate=10000` korunur). Yedekli.
- **Görsel (Extreme + Ultra):** `DragFullWindows="0"`, `ListviewShadow=0`.
- **Game Bar:** `UseNexusForGameBarEnabled=0` (GameDVR kapatma adımında).
- **netsh:** `nonsackrttresiliency=disabled` (orijinal değer `netsh int tcp show global` / WMI ile okunup yedeklenir, Geri Al'da uygulanır).
- **ContentDeliveryManager (Extreme + Ultra, gizlilik adımı):** kilit ekranı önerileri, abonelikli içerik 338387/338388/338389/353698,
  sessiz uygulama yükleme ve Başlat önerileri = 0.
- **Temizlik:** `%LOCALAPPDATA%\CrashDumps` ve `%WINDIR%\SoftwareDistribution\Download` (1 günden eski, kilitli dosyalar atlanır, servis durdurulmaz).
- **Opsiyonel (varsayılan KAPALI):** "Windows bileşen temizliği (DISM, 5-15 dk sürebilir)" → `Dism.exe /Online /Cleanup-Image /StartComponentCleanup`
  (/ResetBase YOK), pencere açılmaz, ilerleme %10 adımlarla loglanır, 30 dk sınır (DISM zorla sonlandırılmaz).
- **Arayüz:** profil kartlarında "ÖNERİLEN" rozeti başlığın altında ayrı satırda; başlıklar 13.5 pt ve satır kaydırmalı; seçim halkası
  ile içerik arasında 10 px boşluk. Anahtar metinleri taşarsa satır kaydırır; alt bilgi satır kaydırır; sonuç ekranındaki 6 düğme dar
  dolgu ile tek satıra sığar; rozet değerleri taşarsa "…" ile kısalır.
- **Discord:** başlık çubuğunda `discord.gg/fivemhater` bağlantısı. Yalnızca tıklanınca varsayılan tarayıcıda açılır
  (`Process.Start` + `UseShellExecute=true`); uygulama açılışında hiçbir şey otomatik açılmaz.
