using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Sync;

namespace DeskBox.UI.Views
{
    public class MobileSyncView : ViewBase
    {
        private readonly LocalSyncCoordinator _sync;
        private readonly CloudSyncAgent _cloud;
        private DarkButton _toggle, _pair, _copy, _revoke, _remove;
        private Label _state, _code, _uri, _stats, _cloudState;
        private DarkList _devices;
        private PairingTicket _ticket;
        private DarkSelect _addresses;
        private readonly Timer _expiryTimer = new Timer { Interval = 1000 };

        public MobileSyncView(Store store, LocalSyncCoordinator sync, CloudSyncAgent cloud = null) : base(store, "DeskBox Mobile / 设备与云同步", "账号云同步 · TLS 本地同步 · 信任设备 · 离线队列")
        {
            _sync = sync; _cloud = cloud; Build(); if (_sync != null) _sync.Changed += OnChanged; if (_cloud != null) _cloud.Changed += OnChanged; RefreshData();
            _expiryTimer.Tick += delegate { if (Visible) UpdatePairingState(); }; _expiryTimer.Start();
        }

        private void Build()
        {
            var top = new Panel { Dock=DockStyle.Top, Height=240, BackColor=Palette.Bg };
            _toggle=new DarkButton("启动 Local Sync"){Primary=true,Left=0,Top=2,Width=140};
            _pair=new DarkButton("生成配对码"){Left=148,Top=2,Width=110};
            _copy=new DarkButton("复制配对链接"){Left=266,Top=2,Width=120};
            _revoke=new DarkButton("撤销设备"){Left=394,Top=2,Width=90};
            _remove=new DarkButton("移除设备"){Left=492,Top=2,Width=90};
            var conflicts = new DarkButton("处理同步冲突") { Left=590, Top=2, Width=130, Palette=Palette };
            conflicts.Click += delegate { using (var dialog = new SyncConflictsForm(_sync, Palette)) dialog.ShowDialog(this); RefreshData(); };
            _state=new Label{Left=0,Top=48,Width=1100,Height=22,ForeColor=Palette.TextDim,BackColor=Palette.Bg,Font=new Font("Consolas",9F)};
            _code=new Label{Left=0,Top=76,Width=260,Height=38,ForeColor=Palette.Accent,BackColor=Palette.Bg,Font=new Font("Consolas",20F,FontStyle.Bold)};
            _uri=new Label{Left=270,Top=78,Width=850,Height=36,ForeColor=Palette.TextFaint,BackColor=Palette.Bg,Font=new Font("Consolas",8F),AutoEllipsis=true};
            _stats=new Label{Left=0,Top=126,Width=1100,Height=24,ForeColor=Palette.TextFaint,BackColor=Palette.Bg,Font=new Font("Microsoft YaHei UI",8.5F)};
            _toggle.Click+=delegate{Toggle();};_pair.Click+=delegate{CreatePairing();};_copy.Click+=delegate{CopyUri();};_revoke.Click+=delegate{Revoke();};_remove.Click+=delegate{Remove();};
            top.Controls.AddRange(new Control[]{_toggle,_pair,_copy,_revoke,_remove,_state,_code,_uri,_stats});
            top.Controls.Add(conflicts); top.AutoScroll = true;
            _addresses = new DarkSelect(new string[0]) { Left=730,Top=2,Width=250,Palette=Palette };
            _addresses.SelectedIndexChanged += delegate { if(_ticket!=null && _sync.Running && _sync.Devices.IsPairingValid(_ticket)) { try {_uri.Text=_sync.PairingUri(_ticket,_addresses.SelectedText);}catch(Exception ex){_state.Text=ex.Message;} } };
            top.Controls.Add(_addresses);
            var cloudButton = new DarkButton("账号云同步") { Left=0,Top=164,Width=148,Palette=Palette,Primary=true };
            cloudButton.Click += delegate { OpenCloudSettings(); }; top.Controls.Add(cloudButton);
            _cloudState=new Label{Left=0,Top=204,Width=1100,Height=25,ForeColor=Palette.Accent};top.Controls.Add(_cloudState);
            top.Controls.Add(new Label { Left=160,Top=170,Width=820,Height=22,Text="不同网络推荐：私人云账号 · 首次授权后自动同步 · 无需 VPN",ForeColor=Palette.TextDim });
            _devices=new DarkList{Dock=DockStyle.Fill,Palette=Palette,RowHeight=38};
            _devices.SetColumns(new[]{"设备","平台","状态","最近活动","到期"},new[]{260,100,100,180,180});
            var guide = new Label { Dock=DockStyle.Bottom, Height=154, Padding=new Padding(10), ForeColor=Palette.TextDim, BackColor=Palette.CardBg, Font=new Font("Microsoft YaHei UI",9F), Text=
                "不同网络：点击“账号云同步”，在私人网页登录同一账号，分别授权主电脑与 iPhone；首次导入后自动同步，无需 VPN。\r\n局域网直连：启动 Local Sync → 选择可访问的 IP → 生成并复制完整配对链接 → 手机连接设置粘贴。\r\n手机默认前台自动同步，编辑后自动上传；无需反复配对或点击刷新。\r\n配对码 5 分钟有效且仅一次使用。iOS 锁屏后不保证持续同步。\r\nAI 手机中继尚未开放。" };
            Body.Controls.Add(guide);
            Body.Controls.Add(_devices);Body.Controls.Add(top);_devices.BringToFront();
        }

        private void Toggle()
        {
            try
            {
                if(_sync.Running){_sync.Stop();Store.Settings.LocalSyncEnabled=false;}
                else{_sync.Start();Store.Settings.LocalSyncEnabled=true;}
                Store.SaveSettingsOnly();Store.Log("info","sync",_sync.Running?"Local Sync 已启动":"Local Sync 已停止");
            }catch(Exception ex){RefreshData();_state.Text="服务操作失败："+ex.Message;Store.Log("error","sync",ex.Message);return;}RefreshData();
        }

        private void OpenCloudSettings()
        {
            if (_cloud == null) return;
            using (var dialog = new Form { Text="账号云同步", Size=new Size(660,430), StartPosition=FormStartPosition.CenterParent, BackColor=Palette.Bg, ForeColor=Palette.Text, MinimizeBox=false, MaximizeBox=false })
            {
                var description = new Label { Left=20,Top=18,Width=600,Height=66,Text="在私人云端网页登录并授权 Windows 主电脑，将连接凭证粘贴到下方。\r\n首次导入后每15秒自动同步项目、任务和事件；凭证使用 Windows DPAPI 加密保存。\r\n电脑离线时手机可查看云端快照，新提交等待电脑上线处理。",ForeColor=Palette.TextDim };
                var address = new DarkInput { Left=20,Top=88,Width=600,Height=32,Palette=Palette,Text=_cloud.AccountUrl,Placeholder="部署脚本生成的 https://…workers.dev 地址" };
                var input = new DarkInput { Left=20,Top=128,Width=600,Height=88,Palette=Palette,Placeholder="在此粘贴网页登录后复制的 Windows 主电脑连接凭证" };
                input.Inner.Multiline=true; input.Inner.ScrollBars=ScrollBars.Vertical; input.Inner.UseSystemPasswordChar=false;
                input.PerformLayout();
                var status = new Label { Left=20,Top=276,Width=600,Height=70,Text=_cloud.Status,ForeColor=Palette.TextDim };
                var login = new DarkButton("打开账号登录页") { Left=20,Top=232,Width=150,Palette=Palette };
                login.Click += delegate { if (!_cloud.SetAccountUrl(address.Text)) { status.Text="先填写部署脚本生成的 https://…workers.dev 云服务地址"; return; } System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_cloud.AccountUrl) { UseShellExecute=true }); };
                var import = new DarkButton("导入并同步") { Left=182,Top=232,Width=130,Palette=Palette,Primary=true };
                import.Click += async delegate { if (_cloud.Import(input.Text)) { input.Text=""; await _cloud.SyncNow(); } status.Text=_cloud.Status; };
                var logout = new DarkButton("退出云同步") { Left=324,Top=232,Width=120,Palette=Palette };
                logout.Click += delegate { _cloud.Logout(); status.Text=_cloud.Status; };
                dialog.Controls.AddRange(new Control[] {description,address,input,status,login,import,logout});
                Action update = delegate { if (!dialog.IsDisposed) status.Text=_cloud.Status; };
                _cloud.Changed += update;
                try { dialog.ShowDialog(this); } finally { _cloud.Changed -= update; }
            }
        }

        private void CreatePairing(){if(!_sync.Running){_state.Text="请先启动 Local Sync";return;}try{_sync.Devices.CancelPairing(_ticket);_ticket=_sync.CreatePairing(5);_uri.Text=_sync.PairingUri(_ticket,_addresses.SelectedText);RefreshData();}catch(Exception ex){_state.Text=ex.Message;}}
        private void CopyUri(){UpdatePairingState();if(!_copy.Enabled)return;try{Clipboard.SetText(_sync.PairingUri(_ticket,_addresses.SelectedText));_state.Text="已复制：请确认手机能访问所选电脑 IP，且使用完整链接";}catch(Exception ex){_state.Text=ex.Message;}}
        private TrustedDevice Selected(){var row=_devices.SelectedRow;return row==null?null:row.Tag as TrustedDevice;}
        private void Revoke(){var x=Selected();if(x==null)return;if(!Prompt.Confirm(this,"撤销设备","立即撤销「"+x.Name+"」的所有访问 Token？",Palette))return;_sync.Revoke(x.Id);Store.Log("info","sync","撤销设备："+x.Name);RefreshData();}
        private void Remove(){var x=Selected();if(x==null)return;if(!Prompt.Confirm(this,"移除设备","移除「"+x.Name+"」的信任记录？",Palette))return;_sync.Remove(x.Id);RefreshData();}

        public void RefreshData()
        {
            if(_sync==null)return;_toggle.Text=_sync.Running?"停止 Local Sync":"启动 Local Sync";
            var addresses=_sync.LocalAddresses().ToArray();var previous=_addresses.SelectedText;
            if(!_addresses.Inner.Items.Cast<string>().SequenceEqual(addresses)){_addresses.Inner.Items.Clear();_addresses.Inner.Items.AddRange(addresses);_addresses.SelectedIndex=addresses.Length==0?-1:Math.Max(0,Array.IndexOf(addresses,previous));}
            _state.Text=_sync.Running?"ONLINE · TLS "+_sync.Port+" · "+string.Join(" / ",_sync.LocalAddresses().ToArray())+" · FP "+Short(_sync.Thumbprint):"OFFLINE · Local Sync 未启动";
            _state.Text="局域网服务 · "+_state.Text;
            _cloudState.Text="账号云同步 · "+(_cloud==null?"不可用":_cloud.Status)+(_cloud!=null&&!_cloud.LoggedIn?" · 需要授权并导入 Windows 主电脑凭证，手机授权不能代替电脑授权":"");
            _pair.Enabled=_sync.Running;UpdatePairingState();
            var rows=new List<ListRow>();foreach(var d in _sync.Devices.Devices)rows.Add(new ListRow{Icon=d.Revoked?"×":"●",Cells=new[]{d.Name,d.Platform,d.Revoked?"REVOKED":d.ExpiresAt<DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()?"EXPIRED":"TRUSTED",Time(d.LastSeenAt),Time(d.ExpiresAt)},Tag=d,Accent=d.Revoked?Color.FromArgb(248,113,113):(Color?)Palette.Accent});_devices.SetRows(rows);
            _stats.Text="TRUSTED DEVICES "+rows.Count+"  ·  OUTBOUND QUEUE "+_sync.Sync.Queue.Count+"  ·  OPEN CONFLICTS "+_sync.Sync.Conflicts.Count(x=>x.Status=="open")+"  ·  "+(_cloud==null?"CLOUD OFF":_cloud.Status);
        }
        private void UpdatePairingState()
        {
            bool valid = _sync != null && _sync.Running && _sync.Devices.IsPairingValid(_ticket);
            _copy.Enabled = valid;
            _code.Text = valid ? _ticket.Code + "  " + Math.Max(0, (_ticket.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000) + "s" : _ticket == null ? "尚未生成配对码" : "配对码已失效";
            if (!valid) _uri.Text = "重新生成配对码后复制新链接；配对成功、超时或停止服务后旧码不可再用。";
        }
        private static string Time(long ts){return ts<=0?"—":DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime.ToString("MM-dd HH:mm");}
        private static string Short(string s){return string.IsNullOrEmpty(s)?"—":s.Substring(0,Math.Min(12,s.Length));}
        private void OnChanged(){try{if(InvokeRequired)BeginInvoke(new Action(RefreshData));else RefreshData();}catch{}}
        protected override void OnActivated(){RefreshData();}
        protected override void Dispose(bool disposing){if(disposing){_expiryTimer.Stop();_expiryTimer.Dispose();}if(_sync!=null)_sync.Changed-=OnChanged;if(_cloud!=null)_cloud.Changed-=OnChanged;base.Dispose(disposing);}
    }
}
