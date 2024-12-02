using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CheckStock
{
	public class PollingUrlSettings : ConfigurationSection
	{
		[ConfigurationProperty("Urls", IsDefaultCollection = false)]
		[ConfigurationCollection(typeof(UrlCollection), AddItemName = "add")]
		public UrlCollection Urls
		{
			get { return (UrlCollection)this["Urls"]; }
		}
	}

	public class UrlElement : ConfigurationElement
	{
		[ConfigurationProperty("url", IsRequired = true, IsKey = true)]
		public string Url
		{
			get { return (string)this["url"]; }
			set { this["url"] = value; }
		}

		[ConfigurationProperty("selector", IsRequired = true)]
		public string Selector
		{
			get { return (string)this["selector"]; }
			set { this["selector"] = value; }
		}

		[ConfigurationProperty("excludeWord", IsRequired = false)]
		public string ExcludeWord
		{
			get { return (string)this["excludeWord"]; }
			set { this["excludeWord"] = value; }
		}

		[ConfigurationProperty("includeWord", IsRequired = false)]
		public string IncludeWord
		{
			get { return (string)this["includeWord"]; }
			set { this["includeWord"] = value; }
		}

		[ConfigurationProperty("headless", IsRequired = true)]
		public bool Headless
		{
			get { return (bool)this["headless"]; }
			set { this["headless"] = value; }
		}
	}
	public class UrlCollection : ConfigurationElementCollection
	{
		protected override ConfigurationElement CreateNewElement()
		{
			return new UrlElement();
		}

		protected override object GetElementKey(ConfigurationElement element)
		{
			return ((UrlElement)element).Url;
		}

		public IEnumerable<UrlElement> GetElements()
		{
			return this.OfType<UrlElement>();
		}
	}
}
