using System;
using System.IO;
using System.Reflection;

using Microsoft.Extensions.Configuration;

namespace bittorrent.Core;

public interface IConfig<T>
{
	public abstract static T Get();
}

[Config]
public sealed class Config : IConfig<Config>
{
	private static Config? _instance;

	[Section("torrent")]
	public string? DefaultPeerId { get; set; } = null;

	[Section("network")]
	public int DefaultPort { get; set; } = 8080;

	private Config() {}

	public static Config Get()
	{
		if (_instance is null)
			_instance = new Config();
		return _instance;
	}
}

public sealed class ConfigParser
{
	public static T Parse<T>(string path) where T: IConfig<T>
	{
		var ini = new ConfigurationBuilder()
			.AddIniFile(path)
			.Build();
		
		var type = typeof(T);
		if (!type.IsDefined(typeof(ConfigAttribute)))
			throw new InvalidOperationException("Cannot parse a non config class");
		var conf = T.Get();
		foreach (var prop in type.GetProperties())
		{
			try
			{
				var keyName = prop!.Name;
				var section = prop.GetCustomAttribute<SectionAttribute>();
				if (section is not null)
					keyName = $"{section.Section}:{keyName}";
				var value = ini.GetValue(prop.PropertyType, keyName);
				if (value is null)
					continue;
				prop.SetValue(conf, value);
			}
			catch
			{
				continue;
			}
		}
		return conf;
	}
}

[AttributeUsage(AttributeTargets.Class)]
public class ConfigAttribute : Attribute {}
[AttributeUsage(AttributeTargets.Property)]
public class SectionAttribute : Attribute
{
	public string Section { get; }
	public SectionAttribute(string sec) => Section = sec;
}
