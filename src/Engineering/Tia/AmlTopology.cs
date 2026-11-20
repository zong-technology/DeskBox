using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace DeskBox.Engineering.Tia
{
    public sealed class PlcTopologyLink
    {
        public string First = "", Second = "", Kind = "", Source = "";
    }

    public static class AmlTopology
    {
        private const string RolePrefix = "AutomationProjectConfigurationRoleClassLib/";

        internal static void Read(XDocument document, string source, PlcSnapshot snapshot, CancellationToken token)
        {
            if (document.Root == null || document.Root.Name.LocalName != "CAEXFile")
                throw new InvalidDataException("CAx AML 缺少 CAEXFile 根元素。");
            var hierarchy = document.Root.Elements().Where(e => Is(e, "InstanceHierarchy")).ToList();
            var objects = new Dictionary<XElement, PlcObject>();
            var endpoints = new Dictionary<string, List<PlcObject>>(StringComparer.Ordinal);
            int namespaceOffset = snapshot.Objects.Count;
            foreach (var element in hierarchy.SelectMany(e => e.Descendants()).Where(e => Is(e, "InternalElement")))
            {
                token.ThrowIfCancellationRequested();
                string kind = Kind(element);
                string name = (string)element.Attribute("Name");
                if (kind.Length == 0 || string.IsNullOrWhiteSpace(name)) continue;
                var ancestors = element.Ancestors().Take(65).ToList();
                if (ancestors.Count > 64 || snapshot.Objects.Count >= 50000)
                    throw new InvalidDataException("CAx AML 层级或对象数量超过读取上限。");
                var parent = ancestors.FirstOrDefault(objects.ContainsKey);
                var item = new PlcObject
                {
                    Name = name, Kind = kind, Source = Location(source, element),
                    Scope = string.Join("/", ancestors.Where(e => Is(e, "InternalElement")).Reverse().Select(e => (string)e.Attribute("Name"))),
                    AssetId = namespaceOffset + ":" + objects.Count,
                    ParentAssetId = parent == null ? "" : objects[parent].AssetId,
                    HardwareType = Attribute(element, "TypeIdentifier"), OrderNumber = Attribute(element, "OrderNumber"),
                    Firmware = Attribute(element, "FirmwareVersion"), Slot = Attribute(element, "PositionNumber"),
                    Address = Attribute(element, "NetworkAddress")
                };
                if (item.HardwareType.Length == 0) item.HardwareType = Attribute(element, "Type");
                ApplyTypeIdentifier(item);
                item.Content = string.Join(Environment.NewLine, element.Elements().Where(e => Is(e, "Attribute"))
                    .Select(e => (string)e.Attribute("Name") + " · " + Value(e)));
                item.Fingerprint = PlcEngineering.Hash(item.Kind + "\n" + item.Content);
                objects.Add(element, item);
                snapshot.Objects.Add(item);
                string id = (string)element.Attribute("ID");
                if (string.IsNullOrWhiteSpace(id)) continue;
                foreach (var terminal in element.Elements().Where(e => Is(e, "ExternalInterface")))
                {
                    string end = (string)terminal.Attribute("Name");
                    if (string.IsNullOrWhiteSpace(end)) continue;
                    string reference = id + ":" + end;
                    List<PlcObject> matches;
                    if (!endpoints.TryGetValue(reference, out matches))
                        endpoints.Add(reference, matches = new List<PlcObject>());
                    matches.Add(item);
                }
            }
            int omitted = 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in hierarchy.SelectMany(e => e.Descendants()).Where(e => Is(e, "InternalLink")))
            {
                token.ThrowIfCancellationRequested();
                var a = Resolve(endpoints, (string)link.Attribute("RefPartnerSideA"));
                var b = Resolve(endpoints, (string)link.Attribute("RefPartnerSideB"));
                if (a == null || b == null || a == b) { omitted++; continue; }
                string kind = a.Kind == "NetworkPort" && b.Kind == "NetworkPort" ? "端口连线" :
                    (a.Kind == "Subnet" && b.Kind == "NetworkNode") || (a.Kind == "NetworkNode" && b.Kind == "Subnet") ? "子网成员" : "";
                if (kind.Length == 0) { omitted++; continue; }
                string key = string.CompareOrdinal(a.AssetId, b.AssetId) < 0 ? a.AssetId + "/" + b.AssetId : b.AssetId + "/" + a.AssetId;
                if (!seen.Add(key)) continue;
                if (snapshot.TopologyLinks.Count >= 10000) throw new InvalidDataException("CAx AML 已解析连接超过 10000 条。");
                snapshot.TopologyLinks.Add(new PlcTopologyLink { First = a.AssetId, Second = b.AssetId, Kind = kind, Source = Location(source, link) });
            }
            if (omitted > 0) snapshot.Notes.Add(source + " · 未展示 " + omitted + " 条其他类型、缺少端点或端点不唯一的连接。");
            if (objects.Count == 0) snapshot.Notes.Add(source + " · 未发现支持的 Siemens CAx 硬件对象。");
        }

        internal static void ApplyTypeIdentifier(PlcObject item)
        {
            if (!item.HardwareType.StartsWith("OrderNumber:", StringComparison.Ordinal)) return;
            var identifier = item.HardwareType.Substring("OrderNumber:".Length).Split('/');
            if (item.OrderNumber.Length == 0) item.OrderNumber = identifier[0];
            if (item.Firmware.Length == 0 && identifier.Length == 2) item.Firmware = identifier[1];
        }

        private static PlcObject Resolve(Dictionary<string, List<PlcObject>> endpoints, string reference)
        {
            List<PlcObject> found;
            return reference != null && endpoints.TryGetValue(reference, out found) && found.Count == 1 ? found[0] : null;
        }

        private static string Kind(XElement element)
        {
            var roles = new HashSet<string>(element.Elements().Where(e => Is(e, "SupportedRoleClass") || Is(e, "RoleRequirements"))
                .Select(e => (string)(e.Attribute("RefRoleClassPath") ?? e.Attribute("RefBaseRoleClassPath"))), StringComparer.Ordinal);
            if (roles.Contains(RolePrefix + "CommunicationPort")) return "NetworkPort";
            if (roles.Contains(RolePrefix + "Node")) return "NetworkNode";
            if (roles.Contains(RolePrefix + "Subnet")) return "Subnet";
            if (roles.Contains(RolePrefix + "CommunicationInterface")) return "NetworkInterface";
            if (roles.Contains(RolePrefix + "DeviceItem")) return "DeviceItem";
            return roles.Contains(RolePrefix + "Device") ? "Device" : "";
        }

        private static string Attribute(XElement element, string name)
        {
            var attribute = element.Elements().FirstOrDefault(e => Is(e, "Attribute") && (string)e.Attribute("Name") == name);
            return Value(attribute).Trim();
        }
        private static string Value(XElement attribute)
        {
            var value = attribute == null ? null : attribute.Elements().FirstOrDefault(e => Is(e, "Value"));
            return value == null ? "" : value.Value;
        }

        private static bool Is(XElement element, string name) { return element.Name.LocalName == name; }
        private static string Location(string source, XElement element) { return source + ":" + ((IXmlLineInfo)element).LineNumber; }
    }
}
