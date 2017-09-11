using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace JsonConvertTester
{
    class Program
    {
        static void Main(string[] args)
        {
            var ctrl = new MockDeviceCommandProcessor();
            var hr = new CreateMockDeviceCommand<HeartRateDevice>("NEW ID", 80);
            hr.Device.HeartRate = 80;
            var hrCmd = new MockHeartRateDeviceCommand("Test", 80).Do<Discover>().ToMessage();
            ctrl.ProcessMessage(hrCmd);
        }
    }

    public class MockDeviceCommandProcessor
    {
        private readonly MockDeviceController _deviceController;
        private readonly Dictionary<Type, Action<object>> _commandHandlers;
        private readonly Dictionary<Type, Action<MockAbstractDevice>> _deviceStateTransitions;

        public MockDeviceCommandProcessor()
        {
            _deviceController = new MockDeviceController();
            _commandHandlers = GetHandlers();
            _deviceStateTransitions = GetDeviceStates();
        }
        public void ProcessMessage(string json)
        {
            var wrapper = JsonConvert.DeserializeObject<MockDeviceCommandWrapper>(json);
            ProcessWrapper(wrapper);
        }

        private void ProcessAction(object action)
        {
            var actionWrapper = action as MockDeviceCommandWrapper;
            ProcessWrapper(actionWrapper);
        }

        private void ProcessWrapper(MockDeviceCommandWrapper wrapper)
        {
            var cmd = (wrapper.Command as JObject)?.ToObject(wrapper.CommandType);

            //TODO: Check for null

            if (_commandHandlers.ContainsKey(cmd.GetType()))
                _commandHandlers[cmd.GetType()](cmd);
        }

        private Dictionary<Type, Action<object>> GetHandlers()
        {
            return new Dictionary<Type, Action<object>>
            {
                {typeof(MockHeartRateDeviceCommand), MockHeartRateDeviceCommandHandler },
                {typeof(Discover), StateChangeHandler }
            };
        }

        private Dictionary<Type, Action<MockAbstractDevice>> GetDeviceStates()
        {
            return new Dictionary<Type, Action<MockAbstractDevice>>
            {                
                {typeof(Discover), d => d.DoDiscover() },
                {typeof(Lost), d => d.DoLost() }
            };
        }

        public void MockHeartRateDeviceCommandHandler(object obj)
        {
            var cmd = obj as MockHeartRateDeviceCommand;
            if (cmd == null)
            {
                // TODO: Handle error somehow
                return;
            }

            if (!_deviceController.ContainsDeviceWithId(cmd.FriendlyId))
            {
                var hrDevice = new HeartRateDevice(cmd.FriendlyId);
                //CopyPropertiesFromTo(cmd, hrDevice);
                hrDevice.CopyFrom(cmd);
                //var hrDevice = new HeartRateDevice(cmd.FriendlyId, cmd.HeartRate);
                _deviceController.AddOrUpdateDevice(cmd.FriendlyId, hrDevice);
            }
            else
            {
                HeartRateDevice hrDevice;
                if ((hrDevice = _deviceController.GetDevice<HeartRateDevice>(cmd.FriendlyId)) != null)
                {
                    hrDevice.HeartRate = cmd.HeartRate;
                }
            }

            cmd.Actions.ForEach(ProcessAction);
        }

        //private void CopyPropertiesFromTo(object from, object to)
        //{
        //    var fromProps = from.GetType().GetProperties();
        //    var toProps = from.GetType().GetProperties();

        //    foreach (var prop in fromProps)
        //    {
        //        var toProp = toProps.FirstOrDefault(x => x.Name == prop.Name);
               
        //        if (toProp != null && toProp.CanWrite)
        //            toProp.SetValue(to, prop.GetValue(from, null), null);
        //    }
        //}


        public void StateChangeHandler(object obj)
        {
            var action = ConvertObjectTo<MockDeviceAction>(obj);

            if (!_deviceController.ContainsDeviceWithId(action.FriendlyId)) return;

            var device = _deviceController.GetDevice(action.FriendlyId);

            if(_deviceStateTransitions.ContainsKey(action.GetType()))
                _deviceStateTransitions[action.GetType()].Invoke(device);

        }

        private static T ConvertObjectTo<T>(object o)
        {
            //TODO: this will throw an exception if it cannot cast, how should we handle this
            return (T) o;
        }
    }

    public class MockDeviceController
    {
        private readonly Dictionary<string, MockAbstractDevice> _mockDevices = new Dictionary<string, MockAbstractDevice>();

        public bool ContainsDeviceWithId(string id)
        {
            return _mockDevices.ContainsKey(id);
        }

        public void AddOrUpdateDevice(string id, MockAbstractDevice device)
        {
            _mockDevices[id] = device;
        }

        public void RemoveDevice(string id)
        {
            _mockDevices.Remove(id);
        }

        public T GetDevice<T>(string id) where T : MockAbstractDevice
        {
            if (ContainsDeviceWithId(id)) return (T)_mockDevices[id];

            return null;
        }

        public MockAbstractDevice GetDevice(string id)
        {
            if (ContainsDeviceWithId(id)) return _mockDevices[id];

            return null;
        }
    }

    public class MockDeviceCommandWrapper
    {
        public MockDeviceCommandWrapper()
        {
        }

        public MockDeviceCommandWrapper(AbstractMockDeviceCommand command)
        {
            CommandType = command.GetType();
            Command = command;
        }

        public Type CommandType { get; set; }
        public object Command { get; set; }
    }

    public abstract class AbstractMockDeviceCommand
    {
        public string FriendlyId { get; set; }        
    }

    public class MockDeviceCommand : AbstractMockDeviceCommand
    {
        public List<MockDeviceCommandWrapper> Actions = new List<MockDeviceCommandWrapper>();
    }

    public class MockDeviceAction : AbstractMockDeviceCommand
    {
        
    }

    public class MockHeartRateDeviceCommand : MockDeviceCommand
    {
        public int HeartRate { get; set; }
        public MockHeartRateDeviceCommand(string friendlyId, int heartRate)
        {
            if (string.IsNullOrEmpty(friendlyId)) throw new ArgumentNullException(friendlyId, "Mock devices must have a unique ID");

            FriendlyId = friendlyId;
            HeartRate = heartRate;
        }
    }

    public class CreateMockDeviceCommand<T> : MockDeviceCommand where T : MockAbstractDevice
    {        
        public T Device { get; }
        public CreateMockDeviceCommand(string friendlyId, params object[] args)
        {
            Device = (T) Activator.CreateInstance(typeof(T), friendlyId, args);
        }
    }


    public class Discover : MockDeviceAction
    {
    }

    public class Lost : MockDeviceAction
    {
    }

    public class Connecting : MockDeviceAction
    {
    }

    public class Connected : MockDeviceAction
    {
    }

    public class Disconnecting : MockDeviceAction
    {
    }

    public class Disconnected : MockDeviceAction
    {
    }
    public class Interrupted : MockDeviceAction
    {
    }

    public static class DeviceControllerExtensions
    {
        public static string ToMessage(this AbstractMockDeviceCommand command)
        {
            var wrapper = new MockDeviceCommandWrapper { CommandType = command.GetType(), Command = command };
            return JsonConvert.SerializeObject(wrapper);
        }


        public static AbstractMockDeviceCommand Do<T>(this MockDeviceCommand cmd) where T : AbstractMockDeviceCommand, new()
        {
            var action = new T { FriendlyId = cmd.FriendlyId };
            cmd.Actions.Add(new MockDeviceCommandWrapper(action));
            return cmd;
        }
    }

    public abstract class MockAbstractDevice
    {

        protected MockAbstractDevice(string id)
        {
            
        }

        public void DoDiscover()
        {
            
        }

        public void DoLost()
        {
            
        }

        public void DoConnecting()
        {
            
        }

        public void DoConnected()
        {
            
        }

        public void DoDisconnecting()
        {
            
        }

        public void DoDisconnected()
        {
            
        }

        public void DoInterrupted()
        {
            
        }
    }

    public class HeartRateDevice : MockAbstractDevice
    {
        //private static readonly Random Random = new Random();
        public int HeartRate { get; set; }

        public HeartRateDevice(string friendlyId, int heartRate = 70)
            : base(Guid.NewGuid().ToString("N"))
        {
            HeartRate = heartRate;
            FriendlyId = friendlyId;

        }


        public string FriendlyId { get; set; } = "12345";

    }

    public static class ObjectExt
    {
        public static T1 CopyFrom<T1, T2>(this T1 obj, T2 otherObject)
            where T1 : class
            where T2 : class
        {
            PropertyInfo[] srcFields = otherObject.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty);

            PropertyInfo[] destFields = obj.GetType().GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty);

            foreach (var property in srcFields)
            {
                var dest = destFields.FirstOrDefault(x => x.Name == property.Name);
                if (dest != null && dest.CanWrite)
                    dest.SetValue(obj, property.GetValue(otherObject, null), null);
            }

            return obj;
        }
    }
}
