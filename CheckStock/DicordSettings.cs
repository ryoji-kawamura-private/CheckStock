using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CheckStock
{
	using System;
	using System.Configuration;

	public class DiscordSettings : ConfigurationSection
	{
		[ConfigurationProperty("BotToken", IsRequired = true)]
		public string BotToken
		{
			get { return (string)this["BotToken"]; }
			set { this["BotToken"] = value; }
		}

		[ConfigurationProperty("GuildId", IsRequired = true)]
		public ulong GuildId
		{
			get { return ulong.Parse(this["GuildId"].ToString()); }
			set { this["GuildId"] = value.ToString(); }
		}

		[ConfigurationProperty("UserIds", IsDefaultCollection = false)]
		[ConfigurationCollection(typeof(UserIdCollection), AddItemName = "add")]
		public UserIdCollection UserIds
		{
			get => (UserIdCollection)this["UserIds"];
		}
	}

	public class UserIdElement : ConfigurationElement
	{
		[ConfigurationProperty("value", IsRequired = true, IsKey = true)]
		public ulong Value
		{
			get { return ulong.Parse(this["value"].ToString()); }
			set { this["value"] = value.ToString(); }
		}
	}

	public class UserIdCollection : ConfigurationElementCollection
	{
		protected override ConfigurationElement CreateNewElement()
		{
			return new UserIdElement();
		}

		protected override object GetElementKey(ConfigurationElement element)
		{
			return ((UserIdElement)element).Value;
		}

		public IEnumerable<UserIdElement> GetElements()
		{
			return this.OfType<UserIdElement>();
		}
	}
}
