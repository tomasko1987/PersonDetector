using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonDetector
{
	internal class Settings
	{
		public string mqttServer { get; set; }
		public int mqttPort { get; set; }
		public int bufferLimit { get; set; }
		public string S3Bucket { get; set; }
		public string mqttCredentialsName { get; set; }
		public string mqttCredentialsPassword { get; set; }
		public Dictionary<string, string> streams { get; set; }
	}
}
