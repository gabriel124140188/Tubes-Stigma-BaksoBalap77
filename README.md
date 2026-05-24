# TubesSTIMA-BAKSOBALAP-77

## Deskripsi
Repository ini berisi implementasi algoritma greedy pada Robocode Tank Royale untuk memenuhi Tugas Besar Strategi Algoritma. Project terdiri dari satu bot utama dan tiga bot alternatif yang menggunakan pendekatan greedy berbeda.

---

## Penjelasan Algoritma Greedy

### Main Bot : Kusar
Strategi greedy yang digunakan:

- Greedy Target Selection
- Predictive Aiming
- Energy Management
- Orbital Movement
- Wall Avoidance

Penjelasan:

Kusar memilih target berdasarkan skor tertinggi yang dihitung dari beberapa faktor seperti jarak musuh, energi musuh, dan ancaman musuh. Bot juga memprediksi posisi lawan untuk meningkatkan akurasi tembakan serta mengatur penggunaan energi agar lebih efisien.

---

### Alternative Bot 1 : AtapBocor

Strategi greedy:

- Dynamic Target Selection
- Adaptive Movement
- Smart Fire Power

Penjelasan:

AtapBocor memilih target yang paling menguntungkan berdasarkan kondisi permainan saat itu dan menyesuaikan pergerakan untuk menghindari serangan lawan.

---

### Alternative Bot 2 : SiPengkor

Strategi greedy:

- Random Orbital Movement Strategy
- Circular Prediction

Penjelasan:

SiPengkor bergerak mengelilingi musuh secara dinamis agar sulit diprediksi dan menggunakan prediksi arah gerakan musuh sebelum menembak.

---

### Alternative Bot 3 : AlternatifBot

Strategi greedy:

- Utility Score Greedy
- Linear Prediction
- Energy Efficiency

Penjelasan:

AlternatifBot menghitung nilai utility setiap musuh berdasarkan jarak, energi, dan peluang mengenai target. Musuh dengan nilai tertinggi dipilih sebagai target utama.

---

## Requirement Program

Software yang diperlukan:

- .NET SDK 6.0
- Robocode Tank Royale
- Visual Studio / Visual Studio Code
- Git

Default server:

```text
ws://localhost:7654
