using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Etp.Reporting.Desktop.Modules.Reports;
using Etp.Reporting.Reporting;

namespace Etp.Reporting.Desktop.Tests;

public sealed class EveningDsrViewTests
{
    [Theory]
    [InlineData(1366,768)]
    [InlineData(816,480)]
    public void Evening_matrices_render_each_store_at_full_and_compact_sizes(int width,int height)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"Etp.Reporting.slnx")))root=root.Parent;
                var output=Path.Combine(root!.FullName,"artifacts","phase2-review");Directory.CreateDirectory(output);
                var doc=DailySalesReportBuilder.Build(new(2026,8,25),[],[],new Dictionary<string,decimal?>());
                var json=Path.Combine(output,"dsr.json");
                if(File.Exists(json))doc=System.Text.Json.JsonSerializer.Deserialize<DailySalesReportDocument>(File.ReadAllText(json))!;
                else doc=doc with{EveningSheets=[new("WLMHW","Titan World",1600000,1600000m/31,661803,661803m/7,[new("VALUE",34215,null,null,938197,2143453.75m,null,"currency"),new("VOL",5,null,null,196,431,null,"number")])]};
                var view=new DailySalesFocusedView(doc);
                Assert.Equal(doc.EveningSheets.Count,view.Items.Count);
                for(var i=0;i<view.Items.Count;i++)
                {
                    view.SelectedIndex=i;view.Measure(new Size(width,height));view.Arrange(new Rect(0,0,width,height));view.UpdateLayout();
                    Assert.Equal(width,view.ActualWidth);Assert.Equal(height,view.ActualHeight);
                    var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file=File.Create(Path.Combine(output,$"WPF-DSR-{doc.EveningSheets[i].StoreCode}-{width}x{height}.png"));encoder.Save(file);
                    Assert.IsType<ScrollViewer>(((TabItem)view.Items[i]).Content);
                }
            }
            catch(Exception e){failure=e;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if(failure is not null)throw new InvalidOperationException("Evening report layout failed",failure);
    }
}
