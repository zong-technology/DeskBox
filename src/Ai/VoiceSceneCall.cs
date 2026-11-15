using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace DeskBox.Ai
{
    internal sealed class VoiceSceneCall
    {
        public const string ToolName = "deskbox_show_scene";
        public static readonly string[] Allowed = { "avatar", "core", "reactor", "plc", "progress" };
        public readonly string Id, Scene;

        public VoiceSceneCall(string id, string name, string arguments)
        {
            Id = id;
            if (name != ToolName || string.IsNullOrEmpty(arguments) || arguments.Length > 512) return;
            try
            {
                var values = new JavaScriptSerializer().DeserializeObject(arguments) as IDictionary<string, object>;
                object value;
                if (values != null && values.Count == 1 && values.TryGetValue("scene", out value) &&
                    Allowed.Contains(value as string))
                    Scene = (string)value;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
    }
}
