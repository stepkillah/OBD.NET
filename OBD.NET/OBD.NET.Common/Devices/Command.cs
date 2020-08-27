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

        #endregion

        #region Constructors

        public QueuedCommand(string commandText)
        {
            this.CommandText = commandText;

            CommandResult = new CommandResult();
        }

        #endregion
    }
}
