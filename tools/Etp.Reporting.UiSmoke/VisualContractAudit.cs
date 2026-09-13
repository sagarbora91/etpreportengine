using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

internal static class VisualContractAudit
{
    private static readonly List<object> Findings = [];
    private static int textCount;
    private static int targetCount;
    public static void Capture(DependencyObject root,string route)
    {
        foreach(var element in Visuals(root).OfType<FrameworkElement>().Where(element=>element.ActualWidth>0 && element.ActualHeight>0 && element.Visibility==Visibility.Visible))
        {
            if (Ancestors(element).OfType<UIElement>().Any(parent=>parent.Visibility!=Visibility.Visible)) continue;
            if(element is TextBlock text && !string.IsNullOrWhiteSpace(text.Text) && !Ancestors(element).OfType<DataGridRow>().Any())
            {
                if(text.Foreground is not SolidColorBrush foreground || !text.IsEnabled) continue;
                var background=Background(element); var color=Blend(foreground.Color,background,foreground.Opacity);
                var ratio=Contrast(color,background); var required=text.FontSize>=24 || text.FontSize>=18.66 && text.FontWeight.ToOpenTypeWeight()>=700 ? 3 : 4.5;
                textCount++;
                if(ratio+0.001<required || text.FontSize<12)
                    Findings.Add(new { route,kind="text", element=text.Name, label=text.Text.Length<=80 ? text.Text : "Long status/message", size=text.FontSize,ratio=Math.Round(ratio,3),required,foreground=color.ToString(),background=background.ToString() });
            }
            if(element is ButtonBase button && button.IsEnabled && !Ancestors(button).OfType<DataGrid>().Any())
            {
                targetCount++;
                if(button.ActualWidth+0.1<44 || button.ActualHeight+0.1<44)
                    Findings.Add(new { route,kind="target",element=button.Name,label=button.Content is string label ? label : button.GetType().Name,width=button.ActualWidth,height=button.ActualHeight });
            }
        }
    }
    public static void Write(string output)
    {
        if(textCount==0 || targetCount==0) throw new InvalidOperationException("No measured controls; the visual audit did not run.");
        File.WriteAllText(Path.Combine(output,"visual-contract-findings.json"),JsonSerializer.Serialize(new { method="96-DPI offscreen visual-tree measurements; solid-color text composited through parent backgrounds. Disabled text and business-data rows excluded. Not physical input, Narrator, popup or installed acceptance.",textCount,targetCount,findings=Findings },new JsonSerializerOptions{WriteIndented=true}));
    }
    private static Color Background(DependencyObject element)
    {
        var result=Colors.White;
        foreach(var item in Ancestors(element).Reverse())
        {
            var brush=item switch { Border border=>border.Background, Panel panel=>panel.Background, Control control=>control.Background, TextBlock text=>text.Background, _=>null };
            if(brush is SolidColorBrush solid) result=Blend(solid.Color,result,solid.Opacity);
        }
        return result;
    }
    private static Color Blend(Color foreground,Color background,double opacity)
    {
        var alpha=foreground.A/255d*opacity;
        byte Channel(byte front,byte back)=>(byte)Math.Round(front*alpha+back*(1-alpha));
        return Color.FromRgb(Channel(foreground.R,background.R),Channel(foreground.G,background.G),Channel(foreground.B,background.B));
    }
    private static double Contrast(Color first,Color second)
    {
        double L(Color color) { double C(byte value) {var part=value/255d;return part<=0.04045 ? part/12.92 : Math.Pow((part+0.055)/1.055,2.4);} return 0.2126*C(color.R)+0.7152*C(color.G)+0.0722*C(color.B); }
        var a=L(first);var b=L(second);return (Math.Max(a,b)+0.05)/(Math.Min(a,b)+0.05);
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for(var parent=VisualTreeHelper.GetParent(element);parent is not null;parent=VisualTreeHelper.GetParent(parent))yield return parent;
    }
    private static IEnumerable<DependencyObject> Visuals(DependencyObject root)
    {
        for(var index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
        {var child=VisualTreeHelper.GetChild(root,index);yield return child;foreach(var item in Visuals(child))yield return item;}
    }
}
