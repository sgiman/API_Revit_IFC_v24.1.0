/*******************************************************************************
 * ........
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
//
// Revit IFC Import library: this library works with Autodesk(R) Revit(R) to import IFC files.
// Copyright (C) 2013  Autodesk, Inc.
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
//
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Revit.IFC.Common.Enums;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.IFC;
using Revit.IFC.Common.Utility;
using Revit.IFC.Import.Enums;
using Revit.IFC.Import.Utility;

namespace Revit.IFC.Import.Data
{
   /// <summary>
   /// From spec:
   /// Из спецификации:
   /// Представление концепции идентифицированного дискретного, 
   /// почти однородного геологического объекта либо с неравномерной твердой поверхностью, 
   /// либо с верхней поверхностью типа «Ябуки».
   /// фигура или обычная воксельная кубическая форма. Страта представлена как дискретная сущность, 
   /// специализированная (подтипированная) от IfcElement. Слой может быть разрушен
   /// на более мелкие объекты, если свойства различаются в разных слоях или, альтернативно, 
   /// свойства могут быть описаны с помощью ограниченных числовых диапазонов. Слой может нести
   /// информация о физической форме и ее интерпретация как геологического объекта (GML). 
   /// Используемые представления формы должны соответствовать подтипу
   /// IfcGeotechnicalAssembly, в которой это происходит
   /// </summary>
   
   
   public class IFCGeotechnicalStratum : IFCGeotechnicalElement
   {
      /// <summary>
      /// Default Constructor
      /// </summary>
      protected IFCGeotechnicalStratum()
      {
      }

      /// <summary>
      /// Constructs an ifcGeometricStratum from the supplied handle.
      /// </summary>
      /// <param name="ifcGeotechnicalStratum">The handle to use for the Geotechnical Stratum.</param>
      protected IFCGeotechnicalStratum(IFCAnyHandle ifcGeotechnicalStratum)
      {
         Process(ifcGeotechnicalStratum);
      }

      /// <summary>
      /// Processes IfcGeotechnicalStratum attributes.
      /// </summary>
      /// <param name="ifcGeotechnicalStratum">Handle to process.</param>
      protected override void Process(IFCAnyHandle ifcGeotechnicalStratum)
      {
         base.Process(ifcGeotechnicalStratum);
      }

      /// <summary>
      /// Processes IfcGeotechnicalStratum object.
      /// </summary>
      /// <param name="ifcGeotechnicalStratum">Geotechnical Stratum handle to process.</param>
      /// <returns></returns>
      public static IFCGeotechnicalStratum ProcessIFCGeotechnicalStratum(IFCAnyHandle ifcGeotechnicalStratum)
      {
         if (IFCAnyHandleUtil.IsNullOrHasNoValue(ifcGeotechnicalStratum))
         {
            Importer.TheLog.LogNullError(IFCEntityType.IfcElement);
            return null;
         }

         try
         {
            IFCEntity cachedIFCGeotechnicalStratum;
            IFCImportFile.TheFile.EntityMap.TryGetValue(ifcGeotechnicalStratum.StepId, out cachedIFCGeotechnicalStratum);
            if (cachedIFCGeotechnicalStratum != null)
               return (cachedIFCGeotechnicalStratum as IFCGeotechnicalStratum);

            // other subclasses not handled yet.
            if (IFCAnyHandleUtil.IsValidSubTypeOf(ifcGeotechnicalStratum, IFCEntityType.IfcGeotechnicalElement))
               return IFCSolidStratum.ProcessIFCSolidStratum(ifcGeotechnicalStratum);

            return new IFCGeotechnicalStratum(ifcGeotechnicalStratum);
         }
         catch (Exception ex)
         {
            HandleError(ex.Message, ifcGeotechnicalStratum, true);
            return null;
         }
      }
   }
}
