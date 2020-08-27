using System;

namespace OBD.NET.Common.Events.EventArgs
{
	public class SocketErrorEventArgs : System.EventArgs
	{
		public SocketErrorEventArgs( Exception exception )
		{
			this.Exception = exception;
		}

		public Exception Exception { get; }
	}
}