using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyVisionDemo.core
{
    // 定义全局变量，防止被垃圾回收

    internal class HalconProcessor
    {
        // Stack for temporary objects 
        HObject[] OTemp = new HObject[20];

        // Local iconic variables 

        HObject ho_Image, ho_ModelRegion, ho__TmpRegion;
        HObject ho_TemplateImage, ho_MatchContour = null, ho_ROI_0 = null;
        HObject ho_ScanROI_Matched = null, ho_SymbolRegions = null;

        // Local control variables 

        HTuple hv_FolderPath = new HTuple(), hv_AllFiles = new HTuple();
        HTuple hv_ImageFiles = new HTuple(), hv_TestImages = new HTuple();
        HTuple hv_ModelID = new HTuple(), hv_WindowHandle = new HTuple();
        HTuple hv_Width = new HTuple(), hv_Height = new HTuple();
        HTuple hv_i = new HTuple(), hv_MatchResultID = new HTuple();
        HTuple hv_NumMatchResult = new HTuple(), hv_Row = new HTuple();
        HTuple hv_Column = new HTuple(), hv_score = new HTuple();
        HTuple hv_BarCodeHandle = new HTuple(), hv_DecodedDataStrings = new HTuple();
        HTuple hv_result1 = new HTuple(), hv_resultstring1 = new HTuple();
        public void InitModel(string templateImagePath)
        {

            //HOperatorSet.GenRectangle2(out ho_ModelRegion, 1574.75, 1664.15, (new HTuple(91.8017)).TupleRad(), 180.773, 113.322);

            //HOperatorSet.GenRectangle2(out ho__TmpRegion, 1574.12, 1655.95, (new HTuple(-87.1494)).TupleRad(), 138.614, 69.588);

            //HObject ExpTmpOutVar_0;
            //HOperatorSet.Difference(ho_ModelRegion, ho__TmpRegion, out ExpTmpOutVar_0);
            //ho_ModelRegion.Dispose();
            //ho_ModelRegion = ExpTmpOutVar_0;
            ////Matching 01: Reduce the model template
            //ho_TemplateImage.Dispose();
            //HOperatorSet.ReduceDomain(ho_Image, ho_ModelRegion, out ho_TemplateImage);
            ////
            ////Matching 01: Create and train the shape model
            //hv_ModelID.Dispose();
            //HOperatorSet.CreateGenericShapeModel(out hv_ModelID);
            ////Matching 01: set the model parameters
            //HOperatorSet.SetGenericShapeModelParam(hv_ModelID, "contrast_high", 71);
            //HOperatorSet.SetGenericShapeModelParam(hv_ModelID, "contrast_low", 44);
            //HOperatorSet.SetGenericShapeModelParam(hv_ModelID, "metric", "use_polarity");
            //HOperatorSet.TrainGenericShapeModel(ho_TemplateImage, hv_ModelID);


            //ho_ModelRegion.Dispose();
            //ho__TmpRegion.Dispose();

            HOperatorSet.ReadShapeModel(templateImagePath, out hv_ModelID);
        
        }
        public void RunOneImage(string imagePath, out BitmapSource wpfImage, out string barcodeStr, out string statusStr,
            out double centerRow, out double centerCol)
        {
            wpfImage = null;
            barcodeStr = "";
            statusStr = "";
            centerRow = 0;
            centerCol = 0;
            using (HDevDisposeHelper dh = new HDevDisposeHelper())
            {
                
                HOperatorSet.ReadImage(out ho_Image, imagePath);
            }

            //threshold (Image, Region, 200, 255)
            //模糊去噪
            //closing_circle (Region, RegionClosing, 12)

            //--- 2. 实时匹配查找 (放在循环内部) ---
            HOperatorSet.SetGenericShapeModelParam(hv_ModelID, "min_score", 0.65);
            HOperatorSet.SetGenericShapeModelParam(hv_ModelID, "num_matches", 1);
            HOperatorSet.SetGenericShapeModelParam(hv_ModelID, "border_shape_models",
                "false");

            //用当前读到的图片进行搜索
            hv_MatchResultID.Dispose(); hv_NumMatchResult.Dispose();
            HOperatorSet.FindGenericShapeModel(ho_Image, hv_ModelID, out hv_MatchResultID,
                out hv_NumMatchResult);

            //获取匹配结果的中心坐标（如果没匹配到，给个默认坐标，免得ROI画崩溃）
            hv_Row.Dispose();
            hv_Row = 500;
            hv_Column.Dispose();
            hv_Column = 500;
            if ((int)(new HTuple(hv_NumMatchResult.TupleGreater(0))) != 0)
            {
                hv_Row.Dispose();
                HOperatorSet.GetGenericShapeModelResult(hv_MatchResultID, 0, "row", out hv_Row);
                hv_Column.Dispose();
                HOperatorSet.GetGenericShapeModelResult(hv_MatchResultID, 0, "column",
                    out hv_Column);
                hv_score.Dispose();
                HOperatorSet.GetGenericShapeModelResult(hv_MatchResultID, 0, "score", out hv_score);

                //条码检测
                using (HDevDisposeHelper dh = new HDevDisposeHelper())
                {
                   
                    HOperatorSet.GenRectangle1(out ho_ROI_0, hv_Row - 300, hv_Column - 300, hv_Row + 300,
                        hv_Column + 300);
                }
                
                HOperatorSet.ReduceDomain(ho_Image, ho_ROI_0, out ho_ScanROI_Matched);

                //创建一个条码读取模版
                hv_BarCodeHandle.Dispose();
                HOperatorSet.CreateBarCodeModel(new HTuple(), new HTuple(), out hv_BarCodeHandle);

                //核心致命参数配置（极大提高识别率）：
                HOperatorSet.SetBarCodeParam(hv_BarCodeHandle, "element_size_min", 1);
                //允许极细的条
                HOperatorSet.SetBarCodeParam(hv_BarCodeHandle, "element_size_max", 30);
                //允许极宽的条 (应对镜头距离不一致)
                HOperatorSet.SetBarCodeParam(hv_BarCodeHandle, "orientation", 45);
                //允许45角度


                //成功解码到一个条形码后将解码停止
                HOperatorSet.SetBarCodeParam(hv_BarCodeHandle, "stop_after_result_num", 1);

                
                HOperatorSet.FindBarCode(ho_ScanROI_Matched, out ho_SymbolRegions, hv_BarCodeHandle,
                    "Code 128", out hv_DecodedDataStrings);

                //删除条码模版并清除分配的内存
                HOperatorSet.ClearBarCodeModel(hv_BarCodeHandle);
                //获取解码结果
                barcodeStr = hv_DecodedDataStrings.ToString();
                //判断条件
                if ((int)(new HTuple((new HTuple(hv_DecodedDataStrings.TupleLength())).TupleGreater(
                    0))) != 0)
                {
                    hv_result1.Dispose();
                    hv_result1 = 1;
                    hv_resultstring1.Dispose();
                    hv_resultstring1 = "OK";
                    

                }
                else
                {
                    hv_result1.Dispose();
                    hv_result1 = 0;
                    hv_resultstring1.Dispose();
                    hv_resultstring1 = "NG";
                    
                }
                //获取状态结果
                statusStr = hv_resultstring1.ToString();
                barcodeStr = hv_DecodedDataStrings.ToString();
                centerRow = hv_Row.ToDArr()[0]; // 取出找到的标签中心点行坐标
                centerCol = hv_Column.ToDArr()[0];

                // 以下代码是固定公式，用于给您的 WPF 界面显示图
                //HOperatorSet.GetImagePointer1(ho_Image, out HTuple hv_Pointer, out HTuple hv_Type, out HTuple hv_Width, out HTuple hv_Height);
                //wpfImage = BitmapSource.Create(hv_Width, hv_Height, 96, 96, PixelFormats.Gray8, null, hv_Pointer, (int)(hv_Width * hv_Height));

                ho_Image.Dispose();
                ho_ROI_0.Dispose();
                ho_ScanROI_Matched.Dispose();
                ho_SymbolRegions.Dispose(); hv_DecodedDataStrings.Dispose();
            }
        }
    }
}
