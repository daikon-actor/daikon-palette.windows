using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DaikonPalette
{
    internal sealed class PaletteItem
    {
        public PaletteItem(string label, string value)
        {
            Label = label;
            Value = value;
        }

        /// <summary>ボタンに表示する文字</summary>
        public string Label { get; private set; }

        /// <summary>入力する文字（プレースホルダー展開前）</summary>
        public string Value { get; private set; }
    }

    internal sealed class PaletteGroup
    {
        public PaletteGroup(string name, List<PaletteItem> items, bool isTemplate)
        {
            Name = name;
            Items = items;
            IsTemplate = isTemplate;
        }

        public string Name { get; private set; }
        public List<PaletteItem> Items { get; private set; }
        public bool IsTemplate { get; private set; }
    }

    /// <summary>読み込んだ結果</summary>
    internal sealed class PaletteContent
    {
        public PaletteContent()
        {
            Groups = new List<PaletteGroup>();
            Errors = new List<string>();
        }

        public List<PaletteGroup> Groups { get; private set; }

        /// <summary>単語を読み込んだファイル（内蔵の既定を使ったときは null）</summary>
        public string WordsPath { get; set; }

        /// <summary>テンプレートを読み込んだファイル（内蔵の既定を使ったときは null）</summary>
        public string TemplatesPath { get; set; }

        /// <summary>読み込めなかったファイルの説明</summary>
        public List<string> Errors { get; private set; }
    }

    /// <summary>
    /// パレットの内容を読み込む。
    ///   単語: data\palette.json（なければ内蔵の既定）
    ///   テンプレート: data\templates.json（なければ内蔵の既定）
    /// </summary>
    internal static class PaletteData
    {
        public static string DataFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); }
        }

        /// <summary>メモの保存先（data\memo）</summary>
        public static string MemoFolder
        {
            get { return Path.Combine(DataFolder, "memo"); }
        }

        public const string WordsFileName = "palette.json";
        public const string TemplatesFileName = "templates.json";

        public static PaletteContent Load()
        {
            PaletteContent content = new PaletteContent();

            string wordsPath;
            List<KeyValuePair<string, List<string>>> words = ReadFirst(content.Errors, out wordsPath,
                Path.Combine(DataFolder, WordsFileName));
            content.WordsPath = wordsPath;
            if (words == null)
            {
                words = DefaultWords();
            }
            foreach (KeyValuePair<string, List<string>> group in words)
            {
                List<PaletteItem> items = new List<PaletteItem>();
                foreach (string value in group.Value)
                {
                    if (value.Trim().Length > 0)
                    {
                        items.Add(new PaletteItem(Placeholders.Label(value), value));
                    }
                }
                if (items.Count > 0)
                {
                    content.Groups.Add(new PaletteGroup(group.Key, items, false));
                }
            }

            string templatesPath;
            List<KeyValuePair<string, List<string>>> templates = ReadFirst(content.Errors, out templatesPath,
                Path.Combine(DataFolder, TemplatesFileName));
            content.TemplatesPath = templatesPath;
            if (templates == null)
            {
                templates = DefaultTemplates();
            }
            // テンプレートは配列が「行」なので改行で連結する
            List<PaletteItem> templateItems = new List<PaletteItem>();
            foreach (KeyValuePair<string, List<string>> t in templates)
            {
                templateItems.Add(new PaletteItem(t.Key, string.Join("\n", t.Value)));
            }
            if (templateItems.Count > 0)
            {
                content.Groups.Add(new PaletteGroup("テンプレート", templateItems, true));
            }
            return content;
        }

        /// <summary>候補のうち最初に読めたファイルの内容を返す。どれも読めなければ null</summary>
        private static List<KeyValuePair<string, List<string>>> ReadFirst(
            List<string> errors, out string usedPath, params string[] paths)
        {
            foreach (string path in paths)
            {
                List<KeyValuePair<string, List<string>>> data = TryRead(path, errors);
                if (data != null)
                {
                    usedPath = path;
                    return data;
                }
            }
            usedPath = null;
            return null;
        }

        /// <summary>
        /// 編集用のファイル（data フォルダの JSON）を用意してパスを返す。なければ既定の内容で作る。
        /// </summary>
        public static string PrepareWordsFile(string currentPath)
        {
            return PrepareFile(currentPath, WordsFileName,
                "\"グループ名\": [\"単語\", \"単語\"] の形で書きます。保存するとパレットに反映されます。{{date}} {{time}} {{datetime}} {{weekday}} {{cursor}} が使えます。",
                DefaultWords(), false);
        }

        public static string PrepareTemplatesFile(string currentPath)
        {
            return PrepareFile(currentPath, TemplatesFileName,
                "\"テンプレート名\": [\"1行目\", \"2行目\"] の形で書きます。保存するとパレットに反映されます。{{cursor}} が入力後のカーソル位置になります。",
                DefaultTemplates(), true);
        }

        private static string PrepareFile(string currentPath, string fileName, string comment,
            List<KeyValuePair<string, List<string>>> defaults, bool linePerItem)
        {
            if (currentPath != null && File.Exists(currentPath))
            {
                return currentPath;
            }
            string path = Path.Combine(DataFolder, fileName);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllText(path, ToJson(comment, defaults, linePerItem), new UTF8Encoding(false));
            }
            return path;
        }

        /// <summary>
        /// data\memo に「日付.時刻.txt」（例: 20241001.1012.txt）の空のメモを作ってパスを返す。
        /// 同じ名前があれば -2, -3 … を付ける。
        /// </summary>
        public static string CreateMemo(DateTime now)
        {
            Directory.CreateDirectory(MemoFolder);
            string baseName = Placeholders.FormatDate(now, "YYYYMMDD.HHmm");
            for (int n = 1; n < 100; n++)
            {
                string name = n == 1 ? baseName + ".txt" : baseName + "-" + n + ".txt";
                string path = Path.Combine(MemoFolder, name);
                if (!File.Exists(path))
                {
                    using (new FileStream(path, FileMode.CreateNew))
                    {
                    }
                    return path;
                }
            }
            throw new IOException("同じ時刻のメモが多すぎます");
        }

        /// <summary>人が編集しやすい形の JSON を作る</summary>
        private static string ToJson(string comment, List<KeyValuePair<string, List<string>>> data, bool linePerItem)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\r\n  ").Append(Quote("$comment")).Append(": ").Append(Quote(comment));
            foreach (KeyValuePair<string, List<string>> pair in data)
            {
                sb.Append(",\r\n  ").Append(Quote(pair.Key)).Append(": [");
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (linePerItem)
                    {
                        sb.Append(i == 0 ? "\r\n    " : ",\r\n    ");
                    }
                    else if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(Quote(pair.Value[i]));
                }
                sb.Append(linePerItem && pair.Value.Count > 0 ? "\r\n  ]" : "]");
            }
            sb.Append("\r\n}\r\n");
            return sb.ToString();
        }

        private static string Quote(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        /// <summary>{ "名前": "文字列" | ["文字列", ...] } を読む。$ で始まるキーは無視。ファイルがなければ null</summary>
        private static List<KeyValuePair<string, List<string>>> TryRead(string path, List<string> sources)
        {
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    throw new FormatException("最上位が { } ではありません");
                }
                List<KeyValuePair<string, List<string>>> result = new List<KeyValuePair<string, List<string>>>();
                foreach (KeyValuePair<string, object> prop in root)
                {
                    if (prop.Key.StartsWith("$", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    List<string> values = new List<string>();
                    string single = prop.Value as string;
                    IEnumerable array = prop.Value as IEnumerable;
                    if (single != null)
                    {
                        values.Add(single);
                    }
                    else if (array != null)
                    {
                        foreach (object v in array)
                        {
                            string s = v as string;
                            if (s != null)
                            {
                                values.Add(s);
                            }
                        }
                    }
                    result.Add(new KeyValuePair<string, List<string>>(prop.Key, values));
                }
                return result;
            }
            catch (Exception e)
            {
                sources.Add(Path.GetFileName(path) + " を読み込めません: " + e.Message);
                return null;
            }
        }

        private static List<KeyValuePair<string, List<string>>> DefaultWords()
        {
            List<KeyValuePair<string, List<string>>> list = new List<KeyValuePair<string, List<string>>>();
            list.Add(Pair("会議", "議事録", "会議メモ", "打ち合わせ", "議題", "決定事項", "確認事項", "TODO", "出席者", "資料", "次回"));
            list.Add(Pair("状況", "対応中", "完了", "未着手", "保留", "確認済み", "要確認", "至急", "連絡済み"));
            list.Add(Pair("あいさつ", "お疲れ様です。", "お世話になっております。", "よろしくお願いいたします。", "承知しました。", "ありがとうございます。"));
            list.Add(Pair("記号", "■ ", "□ ", "・", "※", "→", "【{{cursor}}】", "「{{cursor}}」", "○", "×"));
            list.Add(Pair("日時", "{{date}}", "{{time}}", "{{datetime}}"));
            return list;
        }

        private static List<KeyValuePair<string, List<string>>> DefaultTemplates()
        {
            List<KeyValuePair<string, List<string>>> list = new List<KeyValuePair<string, List<string>>>();
            list.Add(Pair("会議メモ", "{{date}} 会議メモ", "", "■ 議題", "・{{cursor}}", "", "■ 決定事項", "・", "", "■ TODO", "・"));
            list.Add(Pair("TODO", "■ TODO（{{date}}）", "・{{cursor}}"));
            list.Add(Pair("議事録", "{{date}}（{{weekday}}）議事録", "", "日時：{{datetime}}〜", "場所：", "出席者：", "", "■ 議題", "1. {{cursor}}", "", "■ 内容", "・", "", "■ 決定事項", "・", "", "■ 次回までの対応", "・"));
            return list;
        }

        private static KeyValuePair<string, List<string>> Pair(string key, params string[] values)
        {
            return new KeyValuePair<string, List<string>>(key, new List<string>(values));
        }
    }

    /// <summary>{{date}} などのプレースホルダー</summary>
    internal static class Placeholders
    {
        public const string Cursor = "{{cursor}}";
        private static readonly string[] Weekdays = { "日", "月", "火", "水", "木", "金", "土" };

        public static string FormatDate(DateTime d, string pattern)
        {
            return Regex.Replace(pattern, @"\[([^\]]*)\]|YYYY|MM|M|DD|D|HH|H|mm|ss|ddd", delegate(Match m)
            {
                if (m.Groups[1].Success)
                {
                    return m.Groups[1].Value;
                }
                switch (m.Value)
                {
                    case "YYYY": return d.Year.ToString("D4");
                    case "MM": return d.Month.ToString("D2");
                    case "M": return d.Month.ToString();
                    case "DD": return d.Day.ToString("D2");
                    case "D": return d.Day.ToString();
                    case "HH": return d.Hour.ToString("D2");
                    case "H": return d.Hour.ToString();
                    case "mm": return d.Minute.ToString("D2");
                    case "ss": return d.Second.ToString("D2");
                    case "ddd": return Weekdays[(int)d.DayOfWeek];
                    default: return m.Value;
                }
            });
        }

        public static string Expand(string text, DateTime now)
        {
            return Regex.Replace(text, @"\{\{(date|time|datetime|weekday)\}\}", delegate(Match m)
            {
                switch (m.Groups[1].Value)
                {
                    case "date": return FormatDate(now, "YYYY/MM/DD");
                    case "time": return FormatDate(now, "HH:mm");
                    case "datetime": return FormatDate(now, "YYYY/MM/DD HH:mm");
                    default: return FormatDate(now, "ddd");
                }
            });
        }

        /// <summary>最初の {{cursor}} で前後に分ける（2 つ目以降は消す）</summary>
        public static void SplitCursor(string text, out string before, out string after)
        {
            int i = text.IndexOf(Cursor, StringComparison.Ordinal);
            if (i < 0)
            {
                before = text;
                after = "";
                return;
            }
            before = text.Substring(0, i);
            after = text.Substring(i + Cursor.Length).Replace(Cursor, "");
        }

        /// <summary>ボタンに表示する文字（日付は今の値、{{cursor}} は消す、1 行目だけ）</summary>
        public static string Label(string value)
        {
            string text = Expand(value, DateTime.Now).Replace(Cursor, "");
            string[] lines = InputSender.NormalizeNewlines(text).Split('\n');
            string first = lines[0];
            bool more = lines.Length > 1;
            if (first.Length > 20)
            {
                first = first.Substring(0, char.IsHighSurrogate(first[19]) ? 19 : 20);
                more = true;
            }
            return more ? first + "…" : first;
        }
    }
}
