using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public class DalgeData
{
	public string Id = "";
	public string Type = "";
	public string Text = "";
	public Vector2 Position = Vector2.Zero;
	public Dictionary<int, string> OptionTextByIndex = new();
	public Dictionary<int, string> NextByPort = new();
}

public static class DalgeParser
{
	public static Dictionary<string, DalgeData> Load(string path)
	{
		var nodes = new Dictionary<string, DalgeData>();

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null) throw new System.IO.IOException($"Cannot open {path}: {FileAccess.GetOpenError()}");

		DalgeData currentNode = null;

		while (!file.EofReached())
		{
			string rawLine = file.GetLine();
			string line = rawLine.Trim();

			if (string.IsNullOrWhiteSpace(line))
				continue;

			if (line.StartsWith("[") && line.EndsWith("]"))
			{
				string nodeId = line.Substring(1, line.Length - 2).Trim();

				currentNode = new DalgeData
				{
					Id = nodeId
				};

				if (nodeId.Length == 0 || nodes.ContainsKey(nodeId)) throw new FormatException($"Empty or duplicate dialogue ID: {nodeId}");
				nodes.Add(nodeId, currentNode);
				continue;
			}

			if (currentNode == null)
				continue;

			string[] split = rawLine.Split('=', 2);
			if (split.Length != 2)
				continue;

			string key = split[0].Trim().ToLowerInvariant();
			string value = key == "text" || key.EndsWith("_text") ? split[1] : split[1].Trim();

			switch (key)
			{
				case "type":
					currentNode.Type = value.ToLowerInvariant();
					break;

				case "text":
					currentNode.Text = Unescape(value);
					break;

				case "position":
				{
					string[] posSplit = value.Split(',', 2);

					if (posSplit.Length == 2 &&
						float.TryParse(posSplit[0].Trim(), CultureInfo.InvariantCulture, out float x) &&
						float.TryParse(posSplit[1].Trim(), CultureInfo.InvariantCulture, out float y))
					{
						currentNode.Position = new Vector2(x, y);
					}
					break;
				}

				default:
					if (TryParseOptionKey(key, "option_", "_text", out int optionTextIndex))
					{
						currentNode.OptionTextByIndex[optionTextIndex] = Unescape(value);
					}
					else if (TryParseOptionKey(key, "option_", "_next", out int optionNextIndex))
					{
						currentNode.NextByPort[optionNextIndex] = value;
					}
					else if (key.StartsWith("next_"))
					{
						string portText = key.Substring(5);

						if (int.TryParse(portText, out int port))
							currentNode.NextByPort[port] = value;
					}
					else if (key == "next")
					{
						// Compatibility with the legacy format
						currentNode.NextByPort[0] = value;
					}
					break;
			}
		}

		return nodes;
	}

	public static string Unescape(string text)
	{
		var result = new StringBuilder();
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == '\\' && i + 1 < text.Length)
			{
				if (text[i + 1] == 'n') { result.Append('\n'); i++; continue; }
				if (text[i + 1] == '\\') { result.Append('\\'); i++; continue; }
			}
			result.Append(text[i]);
		}
		return result.ToString();
	}

	private static bool TryParseOptionKey(string key, string prefix, string suffix, out int index)
	{
		index = -1;
		if (!key.StartsWith(prefix) || !key.EndsWith(suffix))
			return false;

		string numberText = key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length);
		return int.TryParse(numberText, out index);
	}
}
