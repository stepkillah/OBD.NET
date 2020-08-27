using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using OBD.NET.Common.Attributes;
using OBD.NET.Common.Commands;
using OBD.NET.Common.Communication;
using OBD.NET.Common.Enums;
using OBD.NET.Common.Events;
using OBD.NET.Common.Events.EventArgs;
using OBD.NET.Common.Extensions;
using OBD.NET.Common.Logging;
using OBD.NET.Common.OBDData;

namespace OBD.NET.Common.Devices
{
	public class ELM327 : SerialDevice
	{
		#region Properties & Fields

		protected readonly Dictionary<Type, IDataEventManager> DataReceivedEventHandlers = new Dictionary<Type, IDataEventManager>();

		protected static Dictionary<Type, int>   PidCache      { get; } = new Dictionary<Type, int>();
		protected static Dictionary<Type, byte?> ModeCache     { get; } = new Dictionary<Type, byte?>();
		protected static Dictionary<int, Type>   DataTypeCache { get; } = new Dictionary<int, Type>();

		protected Mode Mode { get; set; } = Mode.ShowCurrentData; //TODO DarthAffe 26.06.2016: Implement different modes

		protected string MessageChunk { get; set; }

		#endregion

		#region Events

		public delegate void DataReceivedEventHandler<T>( object sender, DataReceivedEventArgs<T> args ) where T : IOBDData;

		public delegate void RawDataReceivedEventHandler( object sender, RawDataReceivedEventArgs args );

		public event RawDataReceivedEventHandler     RawDataReceived;
		public event EventHandler<CanErrorEventArgs> CanError;

		#endregion

		#region Constructors

		public ELM327( ISerialConnection connection, IOBDLogger logger = null )
			: base( connection, logger: logger ) { }

		#endregion

		#region Methods

		public override async Task InitializeAsync()
		{
			await base.InitializeAsync();
			InternalInitialize();
		}

		public override void Initialize()
		{
			base.Initialize();
			InternalInitialize();
		}

		private void InternalInitialize()
		{
			Logger?.WriteLine( "Initializing ...", OBDLogLevel.Debug );

			try
			{
				Logger?.WriteLine( "Resetting Device ...", OBDLogLevel.Debug );
				SendCommand( ATCommand.ResetDevice );

				Logger?.WriteLine( "Turning Echo Off ...", OBDLogLevel.Debug );
				SendCommand( ATCommand.EchoOff );

				Logger?.WriteLine( "Turning Linefeeds Off ...", OBDLogLevel.Debug );
				SendCommand( ATCommand.LinefeedsOff );

				Logger?.WriteLine( "Turning Headers Off ...", OBDLogLevel.Debug );
				SendCommand( ATCommand.HeadersOff );

				Logger?.WriteLine( "Turning Spaced Off ...", OBDLogLevel.Debug );
				SendCommand( ATCommand.PrintSpacesOff );

				Logger?.WriteLine( "Setting the Protocol to 'Auto' ...", OBDLogLevel.Debug );
				SendCommand( ATCommand.SetProtocolAuto );

				WaitQueue();
			}
			// DarthAffe 21.02.2017: This seems to happen sometimes, i don't know why - just retry.
			catch
			{
				Logger?.WriteLine( "Failed to initialize the device!", OBDLogLevel.Error );
				throw;
			}
		}

		protected byte GetModeByte() => (byte) this.Mode;

		/// <summary>
		/// Sends the AT command.
		/// </summary>
		/// <param name="command">The command.</param>
		public virtual void SendCommand( ATCommand command ) => SendCommand( command.Command );

		public virtual async Task<object> SendCommandAsync( string command )
		{
			var commandResult = this.SendCommand( command );

			await commandResult.WaitHandle.WaitAsync();
			return commandResult.Result;
		}

		public virtual void SendCommand( string header, string command, bool waitForResponse = true )
		{
			// First set the header
			this.SendCommand( ATCommand.SetHeader.Command + " " + header );

			// Now send the command
			this.SendCommand( command, waitForResponse );

			// Reset the header
			// this.SendCommand( ATCommand.ResetHeader );
		}

		/// <summary>
		/// Requests the data and calls the handler
		/// </summary>
		/// <typeparam name="T"></typeparam>
		public virtual void RequestData<T>()
			where T : class, IOBDData, new()
		{
			Logger?.WriteLine( "Requesting Type " + typeof( T ).Name + " ...", OBDLogLevel.Debug );

			int   pid  = ResolvePid<T>();
			byte? mode = ResolveMode<T>();

			RequestData( pid, mode );
		}

		/// <summary>
		/// Request data based on a pid
		/// </summary>
		/// <param name="pid">The pid of the requested data</param>
		/// <param name="modeOverride">Optional mode override when sending the command</param>
		public virtual void RequestData( int pid, byte? modeOverride = null )
		{
			Logger?.WriteLine( "Requesting PID " + pid.ToString( "X2" ) + " ...", OBDLogLevel.Debug );
			SendCommand( ( modeOverride ?? this.GetModeByte() ).ToString( "X2" ) + pid.ToString( "X2" ) );
		}

		/// <summary>
		/// Requests the data asynchronous and return the data when available
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public virtual async Task<T> RequestDataAsync<T>()
			where T : class, IOBDData, new()
		{
			Logger?.WriteLine( "Requesting Type " + typeof( T ).Name + " ...", OBDLogLevel.Debug );
			int   pid  = ResolvePid<T>();
			byte? mode = ResolveMode<T>();
			return await RequestDataAsync( pid, mode ) as T;
		}

		/// <summary>
		/// Requests the data asynchronous and return the data when available
		/// </summary>
		/// <returns></returns>
		public virtual async Task<IOBDData> RequestDataAsync( Type type )
		{
			Logger?.WriteLine( "Requesting Type " + type.Name + " ...", OBDLogLevel.Debug );
			int pid = ResolvePid( type );
			return await RequestDataAsync( pid ) as IOBDData;
		}

		/// <summary>
		/// Request data based on a pid
		/// </summary>
		/// <param name="pid">The pid of the requested data</param>
		/// <param name="modeOverride">Optional mode override when sending the command</param>
		public virtual async Task<object> RequestDataAsync( int pid, byte? modeOverride = null )
		{
			Logger?.WriteLine( "Requesting PID " + pid.ToString( "X2" ) + " ...", OBDLogLevel.Debug );
			CommandResult result = SendCommand( ( modeOverride ?? this.GetModeByte() ).ToString( "X2" ) + pid.ToString( "X2" ) );

			await result.WaitHandle.WaitAsync();
			return result.Result;
		}

		protected override object ProcessMessage( string message )
		{
			if ( message == null ) return null;

			DateTime timestamp = DateTime.Now;

			RawDataReceived?.Invoke( this, new RawDataReceivedEventArgs( message, timestamp ) );

			if ( message.ToUpper() == "CAN ERROR" )
			{
				this.CanError?.Invoke( this, new CanErrorEventArgs { Message = message } );
			}
			else if ( message.Length > 4 )
			{
				// DarthAffe 15.08.2020: Splitted messages are prefixed with 0: (first chunk) and 1: (second chunk)
				// DarthAffe 15.08.2020: They also seem to be always preceded by a '009'-message, but since that's to short to be processed it should be safe to ignore.
				// DarthAffe 15.08.2020: Since that behavior isn't really documented (at least I wasn't able to find it) that's all trial and error and might not work for all pids with long results.
				if ( message[ 1 ] == ':' )
				{
					if ( message[ 0 ] == '0' )
						MessageChunk = message.Substring( 2, message.Length - 2 );
					else if ( message[ 0 ] == '1' )
					{
						string fullMessage = MessageChunk + message.Substring( 2, message.Length - 2 );
						MessageChunk = null;
						return ProcessMessage( fullMessage );
					}
				}
				else
				{
					string resModeStr = message.Substring( 0, 2 );
					try
					{
						byte resMode = Convert.ToByte( resModeStr, 16 );

						if ( resMode == this.GetModeByte() + 0x40 || ModeCache.ContainsValue( (byte) ( resMode - 0x40 ) ) )
						{
							byte pid     = (byte) message.Substring( 2, 2 ).GetHexVal();
							int  longPid = message.Substring( 2, 4 ).GetHexVal();
							if ( DataTypeCache.TryGetValue( longPid, out Type dataType ) || DataTypeCache.TryGetValue( pid, out dataType ) )
							{
								if ( ModeCache.TryGetValue( dataType, out var modeByte ) && ( modeByte ?? this.GetModeByte() ) != resMode - 0x40 )
								{
									// Mode didn't match PID
									return null;
								}

								IOBDData obdData = (IOBDData) Activator.CreateInstance( dataType );
								bool     isLong  = obdData.PID == longPid;
								int      start   = isLong ? 6 : 4;
								obdData.Load( message.Substring( start, message.Length - start ) );

								if ( DataReceivedEventHandlers.TryGetValue( dataType, out IDataEventManager dataEventManager ) )
									dataEventManager.RaiseEvent( this, obdData, timestamp );

								if ( DataReceivedEventHandlers.TryGetValue( typeof( IOBDData ), out IDataEventManager genericDataEventManager ) )
									genericDataEventManager.RaiseEvent( this, obdData, timestamp );

								return obdData;
							}
						}
					}
					catch ( FormatException )
					{
						// Ignore format exceptions from convert
					}
				}
			}

			return null;
		}

		protected virtual int ResolvePid<T>()
			where T : class, IOBDData, new()
			=> ResolvePid( typeof( T ) );

		protected virtual int ResolvePid( Type type )
		{
			if ( !PidCache.TryGetValue( type, out int pid ) )
				pid = AddToPidCache( type );

			return pid;
		}

		public virtual int AddToPidCache<T>()
			where T : class, IOBDData, new() => AddToPidCache( typeof( T ) );

		protected virtual int AddToPidCache( Type obdDataType )
		{
			IOBDData data = (IOBDData) Activator.CreateInstance( obdDataType );
			if ( data == null )
				throw new ArgumentException( "Has to implement IOBDData", nameof(obdDataType) );

			int pid = data.PID;

			PidCache.Add( obdDataType, pid );
			DataTypeCache.Add( pid, obdDataType );

			return pid;
		}

		protected virtual byte? ResolveMode<T>()
			where T : class, IOBDData
		{
			if ( !ModeCache.TryGetValue( typeof( T ), out byte? mode ) )
				mode = AddToModeCache<T>();

			return mode;
		}

		public virtual byte? AddToModeCache<T>()
			where T : class, IOBDData => AddToModeCache( typeof( T ) );

		protected virtual byte? AddToModeCache( Type obdDataType )
		{
			var modeAttribute = obdDataType.GetTypeInfo().GetCustomAttribute<ObdModeAttribute>();
			var modeOverride  = modeAttribute?.Mode;

			ModeCache.Add( obdDataType, modeOverride );

			return modeOverride;
		}

		/// <summary>
		/// YOU SHOULDN'T NEED THIS METHOD!
		/// 
		/// You should only use this method if you're requesting data by pid instead of the <see cref="RequestData{T}"/>-method.
		/// 
		/// Initializes the PID-Cache with all IOBDData-Types contained in OBD.NET.
		/// You can add additional ones with <see cref="AddToPidCache{T}"/>.
		/// </summary>
		public virtual void InitializePidCache()
		{
			TypeInfo iobdDataInfo = typeof( IOBDData ).GetTypeInfo();
			foreach ( TypeInfo obdDataType in iobdDataInfo.Assembly.DefinedTypes.Where( t => t.IsClass && !t.IsAbstract && iobdDataInfo.IsAssignableFrom( t ) ) )
				AddToPidCache( obdDataType.AsType() );
		}

		public override void Dispose() => Dispose( true );

		public void Dispose( bool sendCloseProtocol )
		{
			try
			{
				if ( sendCloseProtocol )
					SendCommand( ATCommand.CloseProtocol );
			}
			catch
			{
				/* Well at least we tried ... */
			}

			DataReceivedEventHandlers.Clear();

			base.Dispose();
		}

		public void SubscribeDataReceived<T>( DataReceivedEventHandler<T> eventHandler ) where T : IOBDData
		{
			if ( !DataReceivedEventHandlers.TryGetValue( typeof( T ), out IDataEventManager eventManager ) )
				DataReceivedEventHandlers.Add( typeof( T ), ( eventManager = new GenericDataEventManager<T>() ) );

			( (GenericDataEventManager<T>) eventManager ).DataReceived += eventHandler;
		}

		public void UnsubscribeDataReceived<T>( DataReceivedEventHandler<T> eventHandler ) where T : IOBDData
		{
			if ( DataReceivedEventHandlers.TryGetValue( typeof( T ), out IDataEventManager eventManager ) )
				( (GenericDataEventManager<T>) eventManager ).DataReceived -= eventHandler;
		}

		#endregion
	}
}