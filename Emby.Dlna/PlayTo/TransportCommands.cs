#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Emby.Dlna.Common;
using Emby.Dlna.Ssdp;

namespace Emby.Dlna.PlayTo
{
    public class TransportCommands
    {
        private const string CommandBase = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" + "<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://schemas.xmlsoap.org/soap/envelope/\" SOAP-ENV:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" + "<SOAP-ENV:Body>" + "<m:{0} xmlns:m=\"{1}\">" + "{2}" + "</m:{0}>" + "</SOAP-ENV:Body></SOAP-ENV:Envelope>";
        private List<StateVariable> _stateVariables = new List<StateVariable>();
        private List<ServiceAction> _serviceActions = new List<ServiceAction>();

        public List<StateVariable> StateVariables => _stateVariables;

        public List<ServiceAction> ServiceActions => _serviceActions;

        public static TransportCommands Create(XDocument document)
        {
            var command = new TransportCommands();

            var actionList = document.Descendants(UPnpNamespaces.Svc + "actionList");

            foreach (var container in actionList.Descendants(UPnpNamespaces.Svc + "action"))
            {
                command.ServiceActions.Add(ServiceActionFromXml(container));
            }

            var stateValues = document.Descendants(UPnpNamespaces.ServiceStateTable).FirstOrDefault();

            if (stateValues != null)
            {
                foreach (var container in stateValues.Elements(UPnpNamespaces.Svc + "stateVariable"))
                {
                    command.StateVariables.Add(FromXml(container));
                }
            }

            return command;
        }

        private static ServiceAction ServiceActionFromXml(XElement container)
        {
            var serviceAction = new ServiceAction
            {
                Name = container.GetValue(UPnpNamespaces.Svc + "name"),
            };

            var argumentList = serviceAction.ArgumentList;

            foreach (var arg in container.Descendants(UPnpNamespaces.Svc + "argument"))
            {
                argumentList.Add(ArgumentFromXml(arg));
            }

            return serviceAction;
        }

        private static Argument ArgumentFromXml(XElement container)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            return new Argument
            {
                Name = container.GetValue(UPnpNamespaces.Svc + "name"),
                Direction = container.GetValue(UPnpNamespaces.Svc + "direction"),
                RelatedStateVariable = container.GetValue(UPnpNamespaces.Svc + "relatedStateVariable")
            };
        }

        private static StateVariable FromXml(XElement container)
        {
            var allowedValues = new List<string>();
            var element = container.Descendants(UPnpNamespaces.Svc + "allowedValueList")
                .FirstOrDefault();

            if (element != null)
            {
                var values = element.Descendants(UPnpNamespaces.Svc + "allowedValue");

                allowedValues.AddRange(values.Select(child => child.Value));
            }

            return new StateVariable
            {
                Name = container.GetValue(UPnpNamespaces.Svc + "name"),
                DataType = container.GetValue(UPnpNamespaces.Svc + "dataType"),
                AllowedValues = allowedValues.ToArray()
            };
        }

        public string BuildPost(ServiceAction action, string xmlNamespace)
        {
            var stateString = string.Empty;

            foreach (var arg in action.ArgumentList)
            {
                if (arg.Direction == "out")
                {
                    continue;
                }

                if (arg.Name == "InstanceID")
                {
                    stateString += BuildArgumentXml(arg, "0");
                }
                else
                {
                    stateString += BuildArgumentXml(arg, null);
                }
            }

            return string.Format(CultureInfo.InvariantCulture, CommandBase, action.Name, xmlNamespace, stateString);
        }

        public string BuildPost(ServiceAction action, string xmlNamesapce, object value, string commandParameter = "")
        {
            var stateString = string.Empty;

            foreach (var arg in action.ArgumentList)
            {
                if (arg.Direction == "out")
                {
                    continue;
                }

                if (arg.Name == "InstanceID")
                {
                    stateString += BuildArgumentXml(arg, "0");
                }
                else
                {
                    stateString += BuildArgumentXml(arg, value.ToString(), commandParameter);
                }
            }

            return string.Format(CultureInfo.InvariantCulture, CommandBase, action.Name, xmlNamesapce, stateString);
        }

        public string BuildPost(ServiceAction action, string xmlNamesapce, object value, Dictionary<string, string> dictionary)
        {
            var stateString = string.Empty;

            foreach (var arg in action.ArgumentList)
            {
                if (arg.Name == "InstanceID")
                {
                    stateString += BuildArgumentXml(arg, "0");
                }
                else if (dictionary.ContainsKey(arg.Name))
                {
                    stateString += BuildArgumentXml(arg, dictionary[arg.Name]);
                }
                else
                {
                    stateString += BuildArgumentXml(arg, value.ToString());
                }
            }

            return string.Format(CultureInfo.InvariantCulture, CommandBase, action.Name, xmlNamesapce, stateString);
        }

        private string BuildArgumentXml(Argument argument, string value, string commandParameter = "")
        {
            var state = StateVariables.FirstOrDefault(a => string.Equals(a.Name, argument.RelatedStateVariable, StringComparison.OrdinalIgnoreCase));

            if (state != null)
            {
                var sendValue = state.AllowedValues.FirstOrDefault(a => string.Equals(a, commandParameter, StringComparison.OrdinalIgnoreCase)) ??
                    (state.AllowedValues.Count > 0 ? state.AllowedValues[0] : value);

                return string.Format(CultureInfo.InvariantCulture, "<{0} xmlns:dt=\"urn:schemas-microsoft-com:datatypes\" dt:dt=\"{1}\">{2}</{0}>", argument.Name, state.DataType ?? "string", sendValue);
            }

            return string.Format(CultureInfo.InvariantCulture, "<{0}>{1}</{0}>", argument.Name, value);
        }
    }
}
