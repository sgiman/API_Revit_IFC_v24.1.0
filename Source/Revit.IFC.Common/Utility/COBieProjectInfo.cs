/*******************************************************************************
 * COBieProjectInfo.cs
 * 
 * Autodesk Revit 24.0.4.427 (ENU) - IFC 24.1.1.6 (IFC import/Export) 
 * https://github.com/Autodesk/revit-ifc/releases
 *
 * -----------------------------------------------------------------------------
 * Create Build (API REVIT 2024) 
 * Application (add-ins)
 * -----------------------------------------------------------------------------
 * Visual Studio 2022 
 * C# | .NET 4.8
 * ----------------------------------------------------------------------------- 
 * Writing sgiman @ 2023 
 *******************************************************************************/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Revit.IFC.Common.Utility
{
   public class COBieProjectInfo
   {
      public string BuildingName_Number { get; set; }
      public string BuildingType { get; set; }
      public string BuildingDescription { get; set; }
      public string ProjectName { get; set; }
      public string ProjectDescription { get; set; }
      public string ProjectPhase { get; set; }
      public string SiteLocation { get; set; }
      public string SiteDescription { get; set; }

      public COBieProjectInfo()
      {

      }

      public COBieProjectInfo(string projInfoStr)
      {
         if (!string.IsNullOrEmpty(projInfoStr))
         {
            JavaScriptSerializer js = new JavaScriptSerializer();
            COBieProjectInfo projInfo = js.Deserialize<COBieProjectInfo>(projInfoStr);
            BuildingName_Number = projInfo.BuildingName_Number;
            BuildingType = projInfo.BuildingType;
            BuildingDescription = projInfo.BuildingDescription;
            ProjectName = projInfo.ProjectName;
            ProjectDescription = projInfo.ProjectDescription;
            ProjectPhase = projInfo.ProjectPhase;
            SiteLocation = projInfo.SiteLocation;
            SiteDescription = projInfo.SiteDescription;
         }
      }

      public string ToJsonString()
      {
         JavaScriptSerializer js = new JavaScriptSerializer();
         return js.Serialize(this);
      }
   }
}