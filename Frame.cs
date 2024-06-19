using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;

namespace PersonDetector
{
	internal class Frame
	{
		public string date { get; set; }
		public Mat frame { get; set; }
	}
}
