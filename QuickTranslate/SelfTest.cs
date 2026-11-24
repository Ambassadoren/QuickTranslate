using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuickTranslate;

internal static class SelfTest
{
    public static int Run(string reportPath)
    {
        var results = new List<object>();
        int failures = 0;
        void Test(string name, Action test)
        {
            try { test(); results.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); }
        }
        void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        var window = new IntPtr(123);
        DoubleCopyDetector Ready()
        {
            var detector = new DoubleCopyDetector();
            detector.Process(0xA2, 0x1D, true, false, false, false, 100, window);
            return detector;
        }
        Test("double copy and held key", () =>
        {
            var d = Ready();
            Assert(!d.Process(0x43, 0x2E, true, false, false, false, 200, window), "First press triggered");
            Assert(!d.Process(0x43, 0x2E, true, false, false, false, 220, window), "Auto-repeat triggered");
            d.Process(0x43, 0x2E, false, false, false, false, 240, window);
            Assert(d.Process(0x43, 0x2E, true, false, false, false, 400, window), "Double-copy not recognized");
        });
        Test("slow copy, intervening key, alternate modifiers", () =>
        {
            var d = Ready();
            d.Process(0x43, 0x2E, true, false, false, false, 200, window);
            d.Process(0x43, 0x2E, false, false, false, false, 220, window);
            Assert(!d.Process(0x43, 0x2E, true, false, false, false, 1200, window), "Slow copy triggered");
            d.Process(0x43, 0x2E, false, false, false, false, 1210, window);
            d.Process(0x56, 0x2F, true, false, false, false, 1250, window);
            Assert(!d.Process(0x43, 0x2E, true, false, false, false, 1300, window), "Intervening key ignored");
            d.Process(0x43, 0x2E, false, false, false, false, 1310, window);
            Assert(!d.Process(0x43, 0x2E, true, true, false, false, 1400, window), "Alt combination triggered");
        });
        Test("same source window required", () =>
        {
            var d = Ready();
            d.Process(0x43, 0x2E, true, false, false, false, 200, window);
            d.Process(0x43, 0x2E, false, false, false, false, 220, window);
            Assert(!d.Process(0x43, 0x2E, true, false, false, false, 300, new IntPtr(124)), "Cross-window trigger");
        });
        Test("physical C and separately pressed Ctrl", () =>
        {
            var d = Ready();
            d.Process(0x00, 0x2E, true, false, false, false, 200, window);
            d.Process(0x00, 0x2E, false, false, false, false, 220, window);
            d.Process(0xA2, 0x1D, false, false, false, false, 230, window);
            d.Process(0xA2, 0x1D, true, false, false, false, 300, window);
            Assert(d.Process(0x00, 0x2E, true, false, false, false, 350, window), "Separate Ctrl copy not recognized");
        });
        using var engine = new EngineClient();
        Test("English to Russian", () =>
        {
            string value = engine.TranslateAsync("Hello world! Please open the window.", "en", "ru").GetAwaiter().GetResult();
            Assert(Regex.IsMatch(value, "[А-Яа-я]"), "No Russian output: " + value);
            results.Add(new { sample = "en-ru", output = value });
        });
        Test("Russian to English", () =>
        {
            string value = engine.TranslateAsync("Доброе утро! Мне нужно перевести этот текст.", "ru", "en").GetAwaiter().GetResult();
            Assert(value.Contains("morning", StringComparison.OrdinalIgnoreCase), "Unexpected translation: " + value);
            results.Add(new { sample = "ru-en", output = value });
        });
        Test("paragraphs retained", () =>
        {
            string value = engine.TranslateAsync("Good morning.\n\nThank you!", "en", "ru").GetAwaiter().GetResult();
            Assert(value.Contains("\n\n"), "Paragraphs were lost");
        });
        Test("OCR and image translation", () =>
        {
            using var image = new Bitmap(1100, 240);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White);
                using var font = new Font("Arial", 28);
                graphics.DrawString("Hello world.\nThis is a test image.", font, Brushes.Black, 30, 25);
            }
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            string text = MainWindow.OcrImage(stream.ToArray());
            Assert(text.Contains("Hello", StringComparison.OrdinalIgnoreCase), "OCR failed: " + text);
            string value = engine.TranslateAsync(text, "en", "ru").GetAwaiter().GetResult();
            Assert(Regex.IsMatch(value, "[А-Яа-я]"), "OCR translation failed: " + value);
            results.Add(new { sample = "ocr-en-ru", recognized = text, output = value });
        });
        Test("Russian OCR", () =>
        {
            using var image = new Bitmap(1100, 240);
            using (var graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.White);
                using var font = new Font("Arial", 28);
                graphics.DrawString("Доброе утро!\nЭто проверка распознавания.", font, Brushes.Black, 30, 25);
            }
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            string text = MainWindow.OcrImage(stream.ToArray());
            Assert(text.Contains("утро", StringComparison.OrdinalIgnoreCase), "Russian OCR failed: " + text);
            results.Add(new { sample = "ocr-ru", recognized = text });
        });
        FeatureTests.Run(Test, Assert, reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
