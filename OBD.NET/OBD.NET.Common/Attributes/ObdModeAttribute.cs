using System;

namespace OBD.NET.Common.Attributes
{
	[ AttributeUsage( AttributeTargets.Class ) ]
	public class ObdModeAttribute : Attribute
	{
		public ObdModeAttribute( byte mode )
		{
			this.Mode = mode;
		}

		public byte Mode { get; }
	}
}