// HoldDrag · Platform/LanguagePack.cs
//
// 外部语言包：把「界面翻译」从代码里挪出来。
//
// 之前只有 zh / en 两种语言写死在 Loc.cs 里 —— 想加第三门语言必须改代码、重新编译。
// 现在只要把一个 JSON 丢进 <数据目录>\lang\ 就能用：
//
//   { "id": "ja", "name": "日本語",
//     "map": { "设置…": "設定…", "取消": "キャンセル" } }
//
// 缺的词条自动回退到中文原文（不会露 key，也不会中英混排）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HoldDrag;

internal sealed class LanguagePack
{
	public string Id { get; set; } = "";

	/// <summary>语言自己的名字（下拉框里显示这个，换哪种语言都认得出）。</summary>
	public string Name { get; set; } = "";

	public Dictionary<string, string> Map { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

	public string Path { get; set; } = "";

	private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
	{
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		WriteIndented = true,
		// 语言包是给人手写的：大小写不敏感，写出去用 camelCase（id / name / map）
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>语言包目录：&lt;数据目录&gt;\lang</summary>
	public static string Dir
	{
		get
		{
			try
			{
				return System.IO.Path.Combine(AppPaths.DataDir, "lang");
			}
			catch
			{
				return "lang";
			}
		}
	}

	/// <summary>扫描目录里的所有语言包（坏文件跳过并记日志，不影响启动）。</summary>
	public static List<LanguagePack> LoadAll()
	{
		List<LanguagePack> list = new List<LanguagePack>();
		try
		{
			string dir = Dir;
			if (!Directory.Exists(dir))
			{
				return list;
			}
			foreach (string item in Directory.GetFiles(dir, "*.json"))
			{
				LanguagePack languagePack = TryLoad(item);
				if (languagePack != null)
				{
					list.Add(languagePack);
				}
			}
			list.Sort(delegate(LanguagePack a, LanguagePack b)
			{
				return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
			});
		}
		catch (Exception ex)
		{
			Log.Error(ex);
		}
		return list;
	}

	public static LanguagePack TryLoad(string path)
	{
		try
		{
			string text = File.ReadAllText(path, Encoding.UTF8);
			LanguagePack languagePack = JsonSerializer.Deserialize<LanguagePack>(text, Opts);
			if (languagePack == null)
			{
				return null;
			}
			languagePack.Id = (languagePack.Id ?? "").Trim().ToLowerInvariant();
			if (languagePack.Id.Length == 0 || languagePack.Id == "zh" || languagePack.Id == "en")
			{
				// 内置语言不允许被文件覆盖，否则用户一个坏文件就能把界面搞成乱码
				Log.Error("语言包 id 非法或被内置语言占用，已忽略：" + path);
				return null;
			}
			if (string.IsNullOrWhiteSpace(languagePack.Name))
			{
				languagePack.Name = languagePack.Id;
			}
			if (languagePack.Map == null)
			{
				languagePack.Map = new Dictionary<string, string>(StringComparer.Ordinal);
			}
			languagePack.Path = path;
			return languagePack;
		}
		catch (Exception ex)
		{
			Log.Error("语言包解析失败 " + path + " :: " + ex.Message);
			return null;
		}
	}

	/// <summary>
	/// 生成一个空模板：把目前所有的键（英文表里的键就是全部中文原文）列出来，
	/// 翻译只需要把 value 填上。用 --dumpstrings 调用。
	/// </summary>
	public static int WriteTemplate(string path, IEnumerable<string> keys, string id, string name)
	{
		try
		{
			LanguagePack languagePack = new LanguagePack
			{
				Id = string.IsNullOrWhiteSpace(id) ? "xx" : id,
				Name = string.IsNullOrWhiteSpace(name) ? (id ?? "xx") : name
			};
			foreach (string key in keys)
			{
				languagePack.Map[key] = "";
			}
			string directoryName = System.IO.Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			File.WriteAllText(path, JsonSerializer.Serialize(languagePack, Opts), new UTF8Encoding(false));
			return languagePack.Map.Count;
		}
		catch (Exception ex)
		{
			Log.Error(ex);
			return -1;
		}
	}

	/// <summary>这套语言包里真正翻了多少条（模板里留空的不算）。</summary>
	public int TranslatedCount
	{
		get
		{
			int num = 0;
			foreach (KeyValuePair<string, string> item in Map)
			{
				if (!string.IsNullOrWhiteSpace(item.Value))
				{
					num++;
				}
			}
			return num;
		}
	}
}
