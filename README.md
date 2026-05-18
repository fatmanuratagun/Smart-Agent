#🛡️ Aura — Akıllı Alışveriş Danışmanı

> "Akakçe fiyat listeler. Trendyol satar. **Aura seni korur.**"

Aura, kullanıcıları sahte indirimlerden, bütçe aşımından ve yanlış ürün seçiminden koruyan 
bir yapay zeka destekli e-ticaret danışmanıdır.

## ✨ Özellikler

- 🧠 **Akıllı Danışman** — Eksik bilgi varsa soru sorar, doğrudan ürün önermez.
- ⏱️ Akıllı Hafıza (30 Günlük Depolama):** Herhangi bir üyelik gerektirmeden, kullanıcının yaptığı aramalar ve keşifler 30 gün boyunca yerel hafızada (Aura Arşivi) saklanır.
- 💰 **Bütçe Kalkanı** — Bütçeni aşan ürünleri filtreler, uyarı verir.
- 📊 **Kategori Analizi** — Laptop için FPS/ısınma, tencere için yapışmazlık kriterleri.
- 🗣️ **Yorum Analizi** — Bağımsız forum ve sitelerden gerçek kullanıcı yorumları.
- 🌙 **Karanlık/Aydınlık Tema** — Kullanıcı tercihine göre tema değişir.

  ## 🛠️ Teknolojiler

- **Backend:** ASP.NET Core (C#)
- **AI:** Google Gemini 2.5 Flash
- **Arama:** Serper API (Google Search)
- **Veritabanı:** Firebase Realtime Database
- **Frontend:** Razor Views, HTML/CSS/JS
## 🚀 Kurulum

```bash
# Repoyu klonla
git clone https://github.com/kullaniciadi/aura

# appsettings.json'a API key'lerini ekle
GeminiSettings:ApiKey=...
SerperSettings:ApiKey=...
Firebase URL=...

# Çalıştır
dotnet run
```
## 📸 Ekran Görüntüleri

### Ana Sayfa
![Ana Sayfa](screenshots/home.png)

### Bütçe Uyarısı
![Bütçe Uyarısı](screenshots/budget-warning.png)

### Arama Geçmişi
![Arama Geçmişi](screenshots/history.png)

## 👥 Ekip
-Fatma Nur ATAGÜN 
-Hayrunnisa KAYA

## 🏆 BTK Akademi Hackathon 2026

Bu proje BTK Akademi Hackathon 2026 için geliştirilmiştir.
