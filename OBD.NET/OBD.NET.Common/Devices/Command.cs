using System;

namespace OBD.NET.Common.Devices
{
    /// <summary>
    /// Class used for queued command
    /// </summary>
    public class QueuedCommand
    {
        #region Properties & Fields

        public string CommandText { get; private set; }

        public CommandResult CommandResult   { get; }

		public bool WaitForResponse { get; set; } = true;

        public TimeSpan? Timeout { get; set; }

        #endregion

        #region Constructors

        public QueuedCommand(string commandText, TimeSpan? timeout = default)
        {
            CommandText   = commandText;
            CommandResult = new CommandResult();
			Timeout       = timeout;
		}

        #endregion
    }
}
