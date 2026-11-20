using System;
using System.Collections.Generic;

namespace DeskBox.Ai
{
    // Full transcripts are kept independently of the short on-screen subtitle.
    internal sealed class VoiceConversation
    {
        private readonly ChatService history;
        private readonly RealtimeVoiceSettings profile;
        private readonly Dictionary<string,ChatMessage> inputs=new Dictionary<string,ChatMessage>();
        private ChatMessage user, assistant;
        private string userId="";
        private bool dirty;
        private DateTime lastSave=DateTime.MinValue;
        internal ChatSession Session { get; private set; }
        internal string LastCompletedUserText { get; private set; }
        internal VoiceConversation(ChatService history, RealtimeVoiceSettings profile) { this.history=history;this.profile=profile; }

        internal void Accept(VoicePacket packet)
        {
            if(history==null)return;
            if(packet.Interrupted || packet.ResponseFailed){Finish(assistant,true);assistant=null;Finish(user,false);user=null;userId="";}
            if(packet.InputStarted){
                Finish(user,false);user=NewMessage("user");userId=packet.UserTurnId ?? "";
                if(userId.Length>0)inputs[userId]=user;
            }
            if(!string.IsNullOrEmpty(packet.UserCaption)){
                ChatMessage found;
                if(!string.IsNullOrEmpty(packet.UserTurnId) && inputs.TryGetValue(packet.UserTurnId,out found)){user=found;userId=packet.UserTurnId;}
                if(user==null || (!string.IsNullOrEmpty(packet.UserTurnId) && userId.Length>0 && userId!=packet.UserTurnId)){
                    user=NewMessage("user");userId=packet.UserTurnId ?? "";
                }
                if(!string.IsNullOrEmpty(packet.UserTurnId)){inputs[packet.UserTurnId]=user;userId=packet.UserTurnId;}
                user.Content=packet.UserCaptionDelta ? user.Content+packet.UserCaption : packet.UserCaption;
                if(!packet.UserCaptionDelta)user.Streaming=false;
                Put(user);
            }
            if (user != null && (packet.InputEnded || packet.TurnComplete || !string.IsNullOrEmpty(packet.UserCaption) && !packet.UserCaptionDelta) &&
                !string.IsNullOrWhiteSpace(user.Content))
                LastCompletedUserText = user.Content;
            if(!string.IsNullOrEmpty(packet.Caption)){
                if(assistant==null)assistant=NewMessage("assistant");
                assistant.Content+=packet.Caption;Put(assistant);
            }
            if(packet.TurnComplete){Finish(assistant,false);assistant=null;Finish(user,false);user=null;userId="";Flush(true);}
            Flush(false);
        }

        internal void Tool(string name,string result)
        {
            if(history==null)return;
            var message=NewMessage("tool");message.ToolName=name;message.Content=(result.StartsWith("{\"ok\":true",StringComparison.Ordinal) ? "✅ " : "❌ ")+result;
            message.Streaming=false;Put(message);Flush(true);
        }

        internal void Close()
        {
            Finish(assistant,true);Finish(user,false);assistant=user=null;Flush(true);
        }

        private ChatMessage NewMessage(string role)
        {
            return new ChatMessage {Id="m_"+Guid.NewGuid().ToString("N"),Role=role,Ts=DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                Streaming=true,SpeakerName=role=="assistant" ? profile.AssistantName : ""};
        }
        private void Finish(ChatMessage message,bool interrupted)
        {
            if(message==null || string.IsNullOrEmpty(message.Content))return;message.Streaming=false;message.Interrupted=interrupted;Put(message);
        }
        private void Put(ChatMessage message)
        {
            if(Session==null)Session=history.BeginVoiceSession(profile);
            // Store snapshots so a background chat save never sees a partially updated message.
            history.RecordVoiceMessage(Session,new ChatMessage {Id=message.Id,Role=message.Role,Content=message.Content,Ts=message.Ts,
                Streaming=message.Streaming,Interrupted=message.Interrupted,SpeakerName=message.SpeakerName,ToolName=message.ToolName});
            dirty=true;
        }
        internal void Flush(bool force)
        {
            if(!dirty || (!force && DateTime.UtcNow-lastSave<TimeSpan.FromSeconds(1)))return;
            history.SaveVoiceHistory();lastSave=DateTime.UtcNow;dirty=false;
        }
    }
}
