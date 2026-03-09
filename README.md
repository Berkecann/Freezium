<h1 align="center">
  <img src="Freezium/Resources/Freezium.png" alt="Freezium Banner" width="100"/>
  <br/><br/>
  Freezium
</h1>

<p align="center">
  <a href="https://anizium.co">Anizium.co</a> için bir premium injector. Proxy açıkken site sanki premium üyeliğin varmış gibi davranıyor — oturum açık olduğu sürece animeleri üyelik olmadan izleyebilirsin.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-blue?style=for-the-badge&logo=windows"/>
  <img alt=".NET Framework" src="https://img.shields.io/badge/.NET%20Framework-4.8-purple?style=for-the-badge&logo=dotnet"/>
  <img alt="WPF" src="https://img.shields.io/badge/UI-WPF-blueviolet?style=for-the-badge"/>
  <img alt="License" src="https://img.shields.io/badge/license-Non--Commercial-red?style=for-the-badge"/>
</p>

---

## 📖 İçindekiler

- [Ne İşe Yarıyor?](#-ne-işe-yarıyor)
- [Nasıl Çalışıyor?](#️-nasıl-çalışıyor)
- [Özellikler](#-özellikler)
- [Mimari](#️-mimari)
- [Proje Yapısı](#-proje-yapısı)
- [Kullanılan Teknolojiler](#️-kullanılan-teknolojiler)
- [Kurulum ve Çalıştırma](#-kurulum-ve-nasıl-kullanılır)
- [İlk Çalıştırmada Sertifika](#-ilk-çalıştırmada-sertifika)
- [Ayarlar](#️-ayarlar)
- [Katkıda Bulunmak](#-katkıda-bulunmak)
- [Lisans](#-lisans)

---

## 🤔 Ne İşe Yarıyor?

Freezium, bilgisayarında yerel bir HTTPS proxy açarak tarayıcın ile [Anizium.co](https://anizium.co) arasına giriyor. Proxy aktifken siteye giden ve gelen API trafiğini gerçek zamanlı olarak yakalayıp değiştiriyor.

Bunun pratikte anlamı şu:

- **Hesabında oturum açık olduğu sürece** sitedeki animeleri premium üyelik olmadan izleyebilirsin.
- **Watchlist, Takip ve Favoriler** normalde premium gerektiren özellikler. Anizium'un sunucusu bu işlemleri ücretsiz hesaplarda reddediyor. Freezium bu aksiyonları yakalar ve **kendi bilgisayarındaki yerel bir veritabanına** kaydeder. Listeler site arayüzüne şeffaf biçimde sunulduğu için her şey normal gibi görünür.

> **Sorumluluk Reddi:** Bu proje yalnızca eğitim amaçlı geliştirilmiştir. Bu projenin kullanımından kaynaklanan herhangi bir yasal sorumluluk kabul edilmez.

---

## ⚙️ Nasıl Çalışıyor?

Freezium, `8888` portunda bir **FiddlerCore** tabanlı HTTPS proxy başlatır ve bunu sistem proxy'si olarak kaydeder. Bundan sonra tüm HTTPS trafiği proxy üzerinden geçer; Freezium yalnızca `api.anizium.co` hedefli isteklere dokunur.

```
  Tarayıcı / Anizium
         │
         ▼
  ┌─────────────────┐
  │  Freezium Proxy │  ← FiddlerCore (port 8888)
  └────────┬────────┘
           │  Yalnızca api.anizium.co trafiğine dokunur
     ┌─────┴──────┐
     │            │
     ▼            ▼
  İstek          Yanıt
  Interceptor    Interceptor
     │            │
     ▼            ▼
 Yerel DB      Değiştirilmiş JSON
 (LiteDB)      yanıtı tarayıcıya ilet
```

### İstek Tarafı
- Geçen isteklerden `cf-control` Cloudflare başlığını yakalar ve saklar.
- Watchlist, takip ve favori endpointlerine gelen `POST` isteklerini tespit eder.
- AES ile şifrelenmiş istek gövdesini çözer; anime ID'sini ve işlem tipini (`add` / `delete`) çıkarır.
- Değişikliği yerel LiteDB veritabanına kaydeder.

### Yanıt Tarafı
- Liste sayfaları ve kullanıcı detay yanıtlarını yerel verilerle değiştirir ya da zenginleştirir.
- Add/remove işlemlerinde sunucunun yanıtı yerine sahte bir başarı yanıtı enjekte eder, böylece site arayüzü düzgün güncellenir.
- `user/get` yanıtlarında dönen kullanıcı nesnesine yerel watchlist sayısını ekler.

---

## ✨ Özellikler

| Özellik | Açıklama |
|---|---|
| 👑 **Premium Injection** | API yanıtlarını manipüle ederek siteyi sanki premium hesabın varmış gibi çalıştırır |
| 📂 **Yerel Watchlist / Takip / Favoriler** | Premium gerektiren bu özellikler yerel veritabanına kaydedilir ve site arayüzüne şeffaf şekilde sunulur |
| 🔒 **HTTPS Şifre Çözme** | Yerel olarak oluşturulan ve güvenilir olarak işaretlenen bir Kök CA sertifikasıyla tam SSL deşifrelemesi |
| 📦 **Gömülü Veritabanı** | Harici bir servis gerektirmeyen yerel LiteDB veritabanı |
| 🔑 **AES Payload Çözme** | Anizium istek gövdelerini şifreli gönderiyor; Freezium bunları çözerek işlemi tespit eder |
| 🎭 **Yanıt Enjeksiyonu** | Sunucu yanıtları tarayıcıya ulaşmadan önce yerel verilerle değiştirilir |
| 🖥️ **Sistem Tepsisi** | Bildirim tepsisine küçültülerek çalışır, Start / Stop kontrolü oradan yapılır |
| ⚙️ **Otomatik Ayar Kaydetme** | Ayarlar (`ManipulateWL` gibi) değiştirildiği anda otomatik kaydedilir |
| 🪟 **Modern WPF Arayüzü** | Fluent Design karanlık tema, özel başlık çubuğu |

---

## 🏗️ Mimari

Proje **Clean Architecture** prensipleriyle katmanlara ayrılmıştır:

```
┌──────────────────────────────────────┐
│                 UI                   │  WPF MainWindow, TrayIconManager
├──────────────────────────────────────┤
│              Services                │  AppSettingsService
├──────────────────────────────────────┤
│           Infrastructure             │  Proxy, API, Veritabanı, Kripto
├──────────────────────────────────────┤
│                Core                  │  Interface'ler, Modeller, Sabitler
└──────────────────────────────────────┘
```

**Bağımlılık tersine çevirme:** `Infrastructure` katmanı, `Core`'da tanımlanan interface'leri implement eder. UI ve Services katmanları yalnızca bu soyutlamalar üzerinden çalışır.

**Observable Proxy Pattern:** `AppSettings` modeli bir `DispatchProxy` ile sarılır. Herhangi bir property setter çağrıldığında ayarlar otomatik olarak veritabanına yazılır — UI'da manuel "Kaydet" çağrısına gerek yoktur.

---

## 📁 Proje Yapısı

```
Freezium/
├── Core/
│   ├── Constants.cs                # API URL, portlar, dosya yolları gibi sabitler
│   ├── Interfaces/
│   │   ├── IAnimeApiClient.cs      # Uzak API client için kontrat
│   │   ├── IAnimeRepository.cs     # Yerel anime verisi için kontrat (watchlist, takip, favoriler)
│   │   └── ISettingsRepository.cs  # Ayar kalıcılığı için kontrat
│   └── Models/
│       ├── Anime.cs                # Anime domain modeli (sezonlar, bölümler vb.)
│       ├── AnimeUser.cs            # Kullanıcı modeli
│       ├── AppSettings.cs          # Ayar modeli + IAppSettings + ObservableProxy
│       └── WatchList.cs            # Hafif watchlist giriş modeli
│
├── Infrastructure/
│   ├── Api/
│   │   └── AniziumApiClient.cs     # Anizium REST API için HTTP client
│   ├── Crypto/
│   │   └── CryptoHelper.cs         # AES şifreleme / çözme
│   ├── Data/
│   │   └── LiteDbRepository.cs     # LiteDB tabanlı repository implementasyonu
│   └── Proxy/
│       ├── ProxyService.cs          # FiddlerCore yaşam döngüsü (başlat, durdur, sertifika)
│       ├── RequestInterceptor.cs    # BeforeRequest işleyici
│       └── ResponseInterceptor.cs   # BeforeResponse işleyici
│
├── Services/
│   └── AppSettingsService.cs       # Global ayar erişimi, otomatik kayıt
│
├── UI/
│   ├── Controls/
│   │   └── TrayIconManager.cs      # Sistem tepsisi, bağlam menüsü
│   └── Themes/
│       └── (Fluent karanlık tema XAML kaynak sözlükleri)
│
├── MainWindow.xaml                 # Ana uygulama penceresi
├── MainWindow.xaml.cs              # Code-behind (yalnızca UI, iş mantığı servislerde)
├── App.xaml / App.xaml.cs          # Uygulama giriş noktası
└── Program.cs                      # Composition root (servis bağlantıları)
```

---

## 🛠️ Kullanılan Teknolojiler

| Paket | Versiyon | Amaç |
|---|---|---|
| **.NET Framework** | 4.8 | Hedef runtime |
| **WPF** | — | UI framework |
| **FiddlerCore** | (bundled) | HTTPS proxy motoru |
| **BCCertMaker** | (bundled) | Kök CA sertifikası oluşturma |
| **LiteDB** | 5.0.21 | Gömülü NoSQL yerel veritabanı |
| **RestSharp** | 112.1.0 | Anizium REST API için HTTP client |
| **Newtonsoft.Json** | 13.0.4 | JSON serileştirme / çözme |
| **Costura.Fody** | 6.0.0 | Tüm DLL'leri tek EXE'ye gömer |
| **Fody** | 6.8.2 | Costura için IL weaver altyapısı |

---

## 🚀 Kurulum ve Çalıştırma

### Gereksinimler

- **Windows 10 / 11** (x64)
- **Visual Studio 2022** — *.NET masaüstü geliştirme* iş yükü yüklü olmalı
- **[.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)**

> İlk çalıştırmada Kök CA sertifikasını yüklemek için yönetici izni gerekiyor.

---

### Kaynaktan Derleme

1. **Repoyu klonla:**

   ```bash
   git clone https://github.com/Berkecann/Freezium.git
   cd Freezium
   ```

2. **NuGet paketlerini geri yükle:**

   Visual Studio'da çözümü açınca otomatik yüklenir. Manuel yapmak istersen:

   ```bash
   nuget restore Freezium.sln
   ```

3. **Derle:**

   ```bash
   msbuild Freezium.sln /p:Configuration=Release /p:Platform="Any CPU"
   ```

   Çıktı şu konumda olacak:
   ```
   Freezium\bin\Release\Freezium.exe
   ```

   > Costura.Fody tüm bağımlılıkları EXE içine gömdüğü için yanına başka DLL koymanı gerekmez.

---

### 🚀 Çalıştırma ve Nasıl Kullanılır?

1. **Freezium'u çalıştırın.**
2. Arayüzden **Start Proxy** butonuna basın. 
   *(Eğer FiddlerCore kök sertifikası daha önce yüklenmediyse Windows sizden onay isteyecektir, işlemi onaylamak için **Evet**'e basın).*
3. Uygulama üzerindeki durum ibaresi **Status: Running** olacaktır.
4. **Anizium.co**'ya gidin ve hesabınıza giriş yapın.
5. Artık animeleri **ücretsiz bir şekilde (premiummuş gibi)** izlemenin keyfini çıkarabilirsiniz!

> [!WARNING]
> **Önemli Kapatma Uyarısı!**
> Freezium'u kullanmayı bitirdiğinizde veya kapatırken kesinlikle **Stop Proxy** butonuna basmanız (veya uygulamayı kendi üzerinden normal bir şekilde kapatmanız) gerekir.
> 
> Normal şartlarda uygulama kapandığında proxy otomatik olarak devre dışı kalır. Ancak elektrik kesintisi, **BSOD (Mavi Ekran)** veya uygulamanın görev yöneticisinden zorla kapatılması gibi beklenmedik çökmelerde Windows sistem proxy ayarı **açık asılı kalabilir**.
> 
> **İnternete erişemiyorsanız Çözüm çok basittir:**
> Windows Ayarları > Ağ ve İnternet > Ara Sunucu (Proxy) yolunu izleyin ve **"Ara sunucu kullan" (Use a proxy server)** seçeneğini manuel olarak **Kapalı** duruma getirin. İnternetiniz anında geri gelecektir.

---

## 🔐 İlk Çalıştırmada Sertifika

Freezium, tarayıcın ile Anizium API'si arasındaki HTTPS trafiğini çözebilmek için yerel bir Kök CA sertifikası oluşturup buna güven eklemeni gerektirir.

Ne olduğu adım adım:

1. `ProxyService.EnsureCertificate()`, `%LocalAppData%\Freezium\FreeziumCert.p12` dosyasını arar.
2. Yoksa `BCCertMaker` ile yeni bir Kök CA oluşturulur ve diske kaydedilir.
3. Sertifika henüz güvenilir değilse bir Windows UAC isteği açılır.
4. Sertifikaya güvenilene kadar proxy başlamayı bekler.

> Sertifika yalnızca yerel HTTPS deşifrelemesi için kullanılır ve hiçbir sunucuya gönderilmez.

---

## ⚙️ Ayarlar

Tüm ayarlar yerel LiteDB veritabanına otomatik kaydedilir. `AppSettingsService.Current` üzerinden her yerden erişilebilir.

| Ayar | Varsayılan | Açıklama |
|---|---|---|
| `ManipulateWL` | `true` | Açıkken watchlist / takip / favori işlemleri yerel veritabanına kaydedilir ve yanıtlar yerel veriden sunulur |
| `CfControl` | *(runtime'da yakalanır)* | Geçen isteklerden yakalanan Cloudflare token'ı, doğrudan API çağrılarında yeniden kullanılır |

Ayarlar ana pencereden veya sistem tepsisi bağlam menüsünden değiştirilebilir. `ObservableProxy<T>` sayesinde her değişiklik anında kaydedilir.

---

## 🤝 Katkıda Bulunmak

Katkılar memnuniyetle karşılanır! Şu şekilde başlayabilirsin:

1. Repoyu **fork**'la.
2. Bir feature branch oluştur: `git checkout -b feature/özellik-adı`
3. Değişikliklerini commit'le: `git commit -m "feat: ne yaptığını açıkla"`
4. Branch'ini push'la: `git push origin feature/özellik-adı`
5. Bir **Pull Request** aç.

### Kod Stili

- Clean Architecture katman ayrımına dikkat et. İş mantığı `Core` veya `Services`'ta kalmalı, `UI` veya `Infrastructure`'a taşmamalı.
- Tüm public tip ve metotlara XML doc yorum satırı ekle.
- `Core` ve `Infrastructure` içinde `System.Windows` veya WPF tiplerine bağımlılık ekleme.
- `MainWindow.xaml.cs`'i ince bir UI controller olarak tut; her şeyi servislere delege et.

---

## 📄 Lisans

Bu proje **Non-Commercial License** ile lisanslanmıştır. Kişisel ve eğitim amaçlı kullanım serbesttir, ancak **ticari kullanım kesinlikle yasaktır** — detaylar için [LICENSE](LICENSE) dosyasına bakabilirsin.

---

<p align="center">
  <a href="https://github.com/Berkecann">Berkecann</a> tarafından geliştirildi
  <br/>
  <b>Sorumluluk Reddi: Bu proje yalnızca eğitim amaçlı geliştirilmiştir. Bu projenin kullanımından kaynaklanan herhangi bir yasal sorumluluk kabul edilmez.</b>
  <br/>
  <b>Disclaimer: This project is for educational purposes only. No legal responsibility is accepted for the use of this project.</b>
</p>
