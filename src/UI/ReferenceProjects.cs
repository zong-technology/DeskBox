using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.UI.Views;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private void Photo(Graphics graphics,Rectangle bounds,int alpha=60)
        {
            if(_factory==null||bounds.Width<=0||bounds.Height<=0)return;float scale=Math.Max(bounds.Width/(float)_factory.Width,bounds.Height/(float)_factory.Height);float width=bounds.Width/scale,height=bounds.Height/scale;graphics.DrawImage(_factory,bounds,new RectangleF((_factory.Width-width)/2,(_factory.Height-height)/2,width,height),GraphicsUnit.Pixel);using(var brush=new SolidBrush(Color.FromArgb(alpha,3,16,26)))graphics.FillRectangle(brush,bounds);
        }
        private void AddProject()
        {
            using(var chooser=new FolderBrowserDialog{Description="关联真实工程项目目录"})if(chooser.ShowDialog(this)==DialogResult.OK){var name=Prompt.Input(this,"新建工程项目","项目名称",Path.GetFileName(chooser.SelectedPath),_p);if(!string.IsNullOrWhiteSpace(name)){var p=_projects.Add(name,chooser.SelectedPath,"PLC");_projects.SetCurrent(p.Id);DataChanged();}}
        }
        private DarkList _projectFiles;
        private void RefreshProjectFiles(){var p=CurrentProject();var rows=new List<ListRow>();if(p!=null)try{rows=Directory.EnumerateFileSystemEntries(p.Root).Where(_projects.Context.CanAccessPath).Take(300).Select(path=>new{Path=path,Info=new FileInfo(path),Dir=Directory.Exists(path)}).Select(x=>new ListRow{Cells=new[]{Path.GetFileName(x.Path),x.Dir?"文件夹":x.Info.Extension.ToUpperInvariant(),x.Dir?"—":(x.Info.Length/1024d).ToString("0.#")+" KB",File.GetLastWriteTime(x.Path).ToString("yyyy-MM-dd HH:mm")},Tag=x.Path}).ToList();}catch(Exception ex){_status.Text="读取项目目录失败："+ex.Message;}if(_projectFiles!=null)_projectFiles.SetRows(rows);}
        private Control BuildCalculator()
        {
            var page=Page("4–20mA / 仪表计算","确定性本地计算 · 支持正/反量程");var toolbar=Toolbar();toolbar.Controls.Add(Button("返回工具矩阵",()=>Navigate("engineering")));toolbar.Controls.Add(Button("压力 / 温度 / PLC",()=>Navigate("tool-workspace"),false,180));
            var grid=new TableLayoutPanel{Dock=DockStyle.Top,Height=400,ColumnCount=2,RowCount=1};grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));var input=Card("4–20 mA 线性量程换算");input.Dock=DockStyle.Fill;var output=Card("计算结果");output.Dock=DockStyle.Fill;
            DarkInput ma=Input("电流 mA"),low=Input("量程下限"),high=Input("量程上限"),value=Input("工程值（反算）");ma.Text="12";low.Text="0";high.Text="100";value.Text="50";Control[] fields={ma,low,high,value};string[] labels={"输入电流 mA","工程量下限","工程量上限","反算工程值"};for(int i=0;i<fields.Length;i++){var label=TextLabel(labels[i],9,false,_p.TextDim);label.SetBounds(18,68+i*55,125,30);fields[i].SetBounds(150,62+i*55,190,34);input.Controls.Add(label);input.Controls.Add(fields[i]);}
            var result=TextLabel("50.00",34,true);result.SetBounds(28,84,460,64);output.Controls.Add(result);var detail=TextLabel("",11,false,_p.Accent);detail.SetBounds(28,160,460,66);output.Controls.Add(detail);var slider=new ReferenceSlider(_p){Minimum=0,Maximum=1000,Value=500,Left=24,Top=260,Width=400,Height=42,BackColor=_p.CardBg};output.Controls.Add(slider);
            Action<bool> calculate=reverse=>{try{double a=Finite(low.Text),b=Finite(high.Text);var answer=reverse?EngineeringTools.ToCurrent(Finite(value.Text),a,b):EngineeringTools.FromCurrent(Finite(ma.Text),a,b);if(answer.Ok&&(double.IsNaN(answer.Value)||double.IsInfinity(answer.Value)||double.IsNaN(answer.Percent)||double.IsInfinity(answer.Percent)))throw new ArgumentException("计算超出数值范围，请检查输入");if(!answer.Ok)throw new ArgumentException(answer.Error);result.Text=answer.Value.ToString("0.###")+(reverse?" mA":"");detail.Text=answer.Percent.ToString("0.##")+"%   ·   "+(answer.Percent<0||answer.Percent>100?"超出量程，请核对":"量程内")+"\r\n4mA = "+a+"   /   20mA = "+b;slider.Value=(int)Math.Max(0,Math.Min(1000,answer.Percent*10));}catch(Exception ex){detail.Text=ex.Message;}};
            var forward=Button("mA → 工程量",()=>calculate(false),true,150);forward.SetBounds(18,310,150,36);var back=Button("工程量 → mA",()=>calculate(true),false,150);back.SetBounds(180,310,150,36);input.Controls.AddRange(new Control[]{forward,back});slider.Scroll+=delegate{ma.Text=(4+slider.Value/1000d*16).ToString("0.###",CultureInfo.InvariantCulture);calculate(false);};grid.Controls.Add(input,0,0);grid.Controls.Add(output,1,0);var body=new Panel{Dock=DockStyle.Fill,AutoScroll=true};body.Controls.Add(grid);AddMain(page,body,toolbar);calculate(false);return page;
        }
        private void LaunchEngineeringTool(string name)
        {
            var file=Path.Combine(AppPaths.DataDir,"engineering-launchers.json");var entries=Json.Load<Dictionary<string,string>>(file,new Dictionary<string,string>());string path;
            if(!entries.TryGetValue(name,out path)||!File.Exists(path))using(var chooser=new OpenFileDialog{Title="为 "+name+" 选择本机程序或快捷方式",Filter="程序或快捷方式|*.exe;*.lnk"}){if(chooser.ShowDialog(this)!=DialogResult.OK)return;path=chooser.FileName;entries[name]=path;Json.Save(file,entries);}
            OpenPath(path);
        }
        private static double Finite(string text){double value;if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||double.IsNaN(value)||double.IsInfinity(value))throw new ArgumentException("请输入有限的有效数值");return value;}
    }
}
