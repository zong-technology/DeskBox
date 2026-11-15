using System;
using System.Drawing;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI.Views
{
    public partial class EngineeringToolkitView
    {
        private DarkInput SignalField(Panel card,string caption,int x,int y,int width,string value)
        {
            card.Controls.Add(new Label{Text=caption,Left=x,Top=y,Width=width,Height=20,ForeColor=Palette.TextDim,BackColor=Palette.CardBg,Font=new Font("Microsoft YaHei UI",9)});
            return Input(card,caption,x,y+24,width,value);
        }
        private void SignalNote(Panel card,string text,int y)
        {
            card.Controls.Add(new Label{Text=text,Left=16,Top=y,Width=card.Width-32,Height=26,ForeColor=Palette.TextFaint,BackColor=Palette.CardBg,Font=new Font("Microsoft YaHei UI",9)});
        }
        private void BuildReferenceSignalPage()
        {
            int total=Math.Max(1000,_page.ClientSize.Width-18),left=(total-14)*55/100,right=total-left-14;
            var scale=Card("4–20 mA 量程工作区","输入电流与工程量上下限，计算结果可反算、复制。",6,8,left,320);
            var ma=SignalField(scale,"输入电流 / mA",16,80,125,"12");var low=SignalField(scale,"工程量下限",153,80,125,"0");var high=SignalField(scale,"工程量上限",290,80,125,"100");var value=SignalField(scale,"反算工程量",16,156,160,"50");
            var forward=new DarkButton("计算工程量"){Palette=Palette,Primary=true,Left=430,Top=104,Width=130,Height=32};var reverse=new DarkButton("反算电流"){Palette=Palette,Left=188,Top=180,Width=130,Height=32};var copy=new DarkButton("复制结果"){Palette=Palette,Left=330,Top=180,Width=110,Height=32,Enabled=false};
            var result=Output(scale,16,230,left-32);result.Text="输入数值后计算";SignalNote(scale,"工程量 = 下限 + (mA − 4) ÷ 16 × (上限 − 下限)",282);
            Action<bool> calculate=back=>{try{var answer=back?EngineeringTools.ToCurrent(D(value.Text),D(low.Text),D(high.Text)):EngineeringTools.FromCurrent(D(ma.Text),D(low.Text),D(high.Text));result.Text=answer.Ok?answer.Value.ToString("0.####")+(back?" mA":"")+"    /    "+answer.Percent.ToString("0.##")+"%"+(answer.Percent<0||answer.Percent>100?"  · 超出量程":"  · 量程内"):answer.Error;copy.Enabled=answer.Ok;}catch(Exception ex){result.Text=ex.Message;copy.Enabled=false;}};forward.Click+=delegate{calculate(false);};reverse.Click+=delegate{calculate(true);};copy.Click+=delegate{Clipboard.SetText(result.Text);};scale.Controls.AddRange(new Control[]{forward,reverse,copy});
            var pt=Card("Pt100 温度 / 电阻","按 IEC 60751 换算，支持 −200…850 °C。",20+left,8,right,320);var temperature=SignalField(pt,"温度 / °C",16,80,140,"100");var resistance=SignalField(pt,"电阻 / Ω",168,80,140,"138.5055");var toResistance=new DarkButton("温度 → 电阻"){Palette=Palette,Primary=true,Left=16,Top=180,Width=140,Height=32};var toTemperature=new DarkButton("电阻 → 温度"){Palette=Palette,Left=168,Top=180,Width=140,Height=32};var ptResult=Output(pt,16,230,right-32);ptResult.Text="选择换算方向";SignalNote(pt,"标准：IEC 60751  ·  R₀ = 100 Ω",282);toResistance.Click+=delegate{try{ptResult.Text=EngineeringTools.Pt100Resistance(D(temperature.Text)).ToString("0.####")+" Ω";}catch(Exception ex){ptResult.Text=ex.Message;}};toTemperature.Click+=delegate{try{ptResult.Text=EngineeringTools.Pt100Temperature(D(resistance.Text)).ToString("0.###")+" °C";}catch(Exception ex){ptResult.Text=ex.Message;}};pt.Controls.AddRange(new Control[]{toResistance,toTemperature});
            var units=Card("压力与温度单位换算","同一输入值可按所选单位分别换算，结果显示原值与目标单位。",6,342,total,230);var amount=SignalField(units,"输入值",16,80,125,"1");
            var pressureFrom=new DarkSelect(new[]{"Pa","kPa","MPa","bar","mbar","psi","kgf/cm²"}){Palette=Palette,Left=153,Top=104,Width=105};var pressureTo=new DarkSelect(new[]{"Pa","kPa","MPa","bar","mbar","psi","kgf/cm²"}){Palette=Palette,Left=270,Top=104,Width=105};pressureFrom.SelectedIndex=3;pressureTo.SelectedIndex=1;
            var pressure=new DarkButton("换算压力"){Palette=Palette,Primary=true,Left=387,Top=104,Width=115,Height=32};var temperatureFrom=new DarkSelect(new[]{"C","F","K"}){Palette=Palette,Left=540,Top=104,Width=85};var temperatureTo=new DarkSelect(new[]{"C","F","K"}){Palette=Palette,Left=637,Top=104,Width=85};temperatureFrom.SelectedIndex=0;temperatureTo.SelectedIndex=1;var convertTemperature=new DarkButton("换算温度"){Palette=Palette,Left=734,Top=104,Width=115,Height=32};var unitResult=Output(units,16,166,total-32);unitResult.Text="选择单位并换算";
            units.Controls.Add(new Label{Text="压力：原单位 → 目标单位",Left=153,Top=80,Width=350,Height=20,ForeColor=Palette.TextDim,BackColor=Palette.CardBg,Font=new Font("Microsoft YaHei UI",9)});
            units.Controls.Add(new Label{Text="温度：原单位 → 目标单位",Left=540,Top=80,Width=310,Height=20,ForeColor=Palette.TextDim,BackColor=Palette.CardBg,Font=new Font("Microsoft YaHei UI",9)});
            pressure.Click+=delegate{try{string a=pressureFrom.Inner.Text,b=pressureTo.Inner.Text;unitResult.Text=amount.Text+" "+a+" = "+EngineeringTools.ConvertPressure(D(amount.Text),a,b).ToString("0.######")+" "+b;}catch(Exception ex){unitResult.Text=ex.Message;}};convertTemperature.Click+=delegate{try{string a=temperatureFrom.Inner.Text,b=temperatureTo.Inner.Text;unitResult.Text=amount.Text+" "+a+" = "+EngineeringTools.ConvertTemperature(D(amount.Text),a,b).ToString("0.####")+" "+b;}catch(Exception ex){unitResult.Text=ex.Message;}};units.Controls.AddRange(new Control[]{pressureFrom,pressureTo,pressure,temperatureFrom,temperatureTo,convertTemperature});
        }
    }
}
