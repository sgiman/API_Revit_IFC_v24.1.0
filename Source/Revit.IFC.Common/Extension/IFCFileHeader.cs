/*******************************************************************************
 ///////////////////////////////////////////////////////////////////////////////
 * IFCFileHeader.cs
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
 * Modification by sgiman @ 2023
 *
 ///////////////////////////////////////////////////////////////////////////////
 *******************************************************************************/
//
// Альтернативная библиотека пользовательского интерфейса экспорта BIM IFC: эта библиотека работает с Autodesk(R) Revit(R)
// и предоставляет альтернативный пользовательский интерфейс для экспорта файлов IFC из Revit.
// Авторские права (C) 2012 Autodesk, Inc.
//
// Эта библиотека является свободным программным обеспечением;
// вы можете распространять его и/или изменяем его в соответствии с условиями GNU Lesser General Public
// Лицензия, опубликованная Фондом свободного программного обеспечения;
// или версия 2.1 Лицензии или (по вашему выбору) любая более поздняя версия.
//
// Эта библиотека распространяется в надежде, что она будет полезна,
// но БЕЗ КАКИХ-ЛИБО ГАРАНТИЙ; даже без подразумеваемой гарантии
// ТОВАРНАЯ ПРИГОДНОСТЬ или ПРИГОДНОСТЬ ДЛЯ ОПРЕДЕЛЕННОЙ ЦЕЛИ. См. GNU
// Меньшая стандартная общественная лицензия для более подробной информации.
//
// Вы должны были получить копию GNU Lesser General Public.
// Лицензия вместе с этой библиотекой; если нет, напишите в Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;


namespace Revit.IFC.Common.Extensions
{
   public class IFCFileHeader
   {
      private Schema m_schema = null;
      private static Guid s_schemaId = new Guid("81527F52-A20F-4BDF-9F01-E7CE1022840A");
      private const String s_FileHeaderMapField = "IFCFileHeaderMapField";  // Не меняйте это имя, оно влияет на схему.
      
      private const String s_FileComment = "FileComment";                   // ****** SGIMAN *******  (field "FileComment")

      private const String s_FileDescription = "FileDescription";
      private const String s_SourceFileName = "SourceFileName";
      private const String s_AuthorName = "AuthorName";
      private const String s_AuthorEmail = "AuthorEmail";
      private const String s_Organization = "Organization";
      private const String s_Authorization = "Authorization";
      private const String s_ApplicationName = "ApplicationName";
      private const String s_VersionNumber = "VersionNumber";
      private const String s_FileSchema = "FileSchema";


      /// <summary>
      /// заголовок файла IFC
      /// </summary>
      public IFCFileHeader()
      {
         if (m_schema == null)
         {
            m_schema = Schema.Lookup(s_schemaId);
         }
         if (m_schema == null)
         {
            SchemaBuilder fileHeaderBuilder = new SchemaBuilder(s_schemaId);
            fileHeaderBuilder.SetSchemaName("IFCFileHeader");
            fileHeaderBuilder.AddMapField(s_FileHeaderMapField, typeof(String), typeof(String));
            m_schema = fileHeaderBuilder.Finish();
         }
      }

      /// <summary>
      /// Получите информацию Заголовка файла от Расширяемого устройства хранения данных в документе Revit. 
      /// Ограниченный всего одним объектом заголовка файла.
      /// </summary>
      /// <param name="document">Документ, хранящий сохраненный адрес.</param>
      /// <param name="schema">Схема для хранения Заголовка файла.</param>
      /// <returns>Список возвратов Заголовков файлов в устройстве хранения данных.</returns>
      private IList<DataStorage> GetFileHeaderInStorage(Document document, Schema schema)
      {
         FilteredElementCollector collector = new FilteredElementCollector(document);
         collector.OfClass(typeof(DataStorage));
         Func<DataStorage, bool> hasTargetData = ds => (ds.GetEntity(schema) != null && ds.GetEntity(schema).IsValid());

         return collector.Cast<DataStorage>().Where<DataStorage>(hasTargetData).ToList<DataStorage>();
      }

      /// <summary>
      /// Обновите заголовок файла (из пользовательского интерфейса) в документе. 
      /// </summary>
      /// <param name="document">Обновите заголовок файла (от UI) в документ.</param>
      /// <param name="fileHeaderItem">Объект Заголовка файла для сохранения.</param>
      public void UpdateFileHeader(Document document, IFCFileHeaderItem fileHeaderItem)
      {
         if (m_schema == null)
         {
            m_schema = Schema.Lookup(s_schemaId);
         }

         if (m_schema != null)
         {
            Transaction transaction = new Transaction(document, "Update saved IFC File Header");
            transaction.Start();

            IList<DataStorage> oldSavedFileHeader = GetFileHeaderInStorage(document, m_schema);
            if (oldSavedFileHeader.Count > 0)
            {
               List<ElementId> dataStorageToDelete = new List<ElementId>();
               foreach (DataStorage dataStorage in oldSavedFileHeader)
               {
                  dataStorageToDelete.Add(dataStorage.Id);
               }
               document.Delete(dataStorageToDelete);
            }

            DataStorage fileHeaderStorage = DataStorage.Create(document);

            Entity mapEntity = new Entity(m_schema);
            IDictionary<string, string> mapData = new Dictionary<string, string>();

            if (fileHeaderItem.FileComment != null) mapData.Add(s_FileComment, fileHeaderItem.FileComment.ToString());  // ****** SGIMAN ******
            if (fileHeaderItem.FileDescriptions.Count > 0) mapData.Add(s_FileDescription, string.Join("|", fileHeaderItem.FileDescriptions.ToArray()));
            if (fileHeaderItem.SourceFileName != null) mapData.Add(s_SourceFileName, fileHeaderItem.SourceFileName.ToString());
            if (fileHeaderItem.AuthorName != null) mapData.Add(s_AuthorName, fileHeaderItem.AuthorName.ToString());
            if (fileHeaderItem.AuthorEmail != null) mapData.Add(s_AuthorEmail, fileHeaderItem.AuthorEmail.ToString());
            if (fileHeaderItem.Organization != null) mapData.Add(s_Organization, fileHeaderItem.Organization.ToString());
            if (fileHeaderItem.Authorization != null) mapData.Add(s_Authorization, fileHeaderItem.Authorization.ToString());
            if (fileHeaderItem.ApplicationName != null) mapData.Add(s_ApplicationName, fileHeaderItem.ApplicationName.ToString());
            if (fileHeaderItem.VersionNumber != null) mapData.Add(s_VersionNumber, fileHeaderItem.VersionNumber.ToString());
            if (fileHeaderItem.FileSchema != null) mapData.Add(s_FileSchema, fileHeaderItem.FileSchema.ToString());

            mapEntity.Set<IDictionary<string, String>>(s_FileHeaderMapField, mapData);
            fileHeaderStorage.SetEntity(mapEntity);

            transaction.Commit();
         }
      }

      /// <summary>
      /// Будет сохранён Заголовок файла IFC
      /// </summary>
      /// <param name="document">Документ, где информация Заголовка файла хранится.</param>
      /// <param name="fileHeader">Вывод сохраненного Заголовка файла от расширяемого устройства хранения данных.</param>
      /// <returns>Состояние, существует ли существующий сохраненный Заголовок файла.</returns>
      public bool GetSavedFileHeader(Document document, out IFCFileHeaderItem fileHeader)
      {
         fileHeader = new IFCFileHeaderItem();

         if (m_schema == null)
         {
            m_schema = Schema.Lookup(s_schemaId);
         }

         if (m_schema == null)
         {
            return false;
         }

         IList<DataStorage> fileHeaderStorage = GetFileHeaderInStorage(document, m_schema);

         if (fileHeaderStorage.Count == 0)
            return false;

         // ожидается только одна информация о Заголовке файла в устройстве хранения данных
         Entity savedFileHeader = fileHeaderStorage[0].GetEntity(m_schema);
         IDictionary<string, string> savedFileHeaderMap = savedFileHeader.Get<IDictionary<string, string>>(s_FileHeaderMapField);
         if (savedFileHeaderMap.ContainsKey(s_FileDescription))
            fileHeader.FileDescriptions = savedFileHeaderMap[s_FileDescription].Split('|').ToList();
         
         if (savedFileHeaderMap.ContainsKey(s_FileComment))                   // ****** SGIMAN ******
            fileHeader.FileComment = savedFileHeaderMap[s_FileComment];
         
         if (savedFileHeaderMap.ContainsKey(s_SourceFileName))
            fileHeader.SourceFileName = savedFileHeaderMap[s_SourceFileName];
         if (savedFileHeaderMap.ContainsKey(s_AuthorName))
            fileHeader.AuthorName = savedFileHeaderMap[s_AuthorName];
         if (savedFileHeaderMap.ContainsKey(s_AuthorEmail))
            fileHeader.AuthorEmail = savedFileHeaderMap[s_AuthorEmail];
         if (savedFileHeaderMap.ContainsKey(s_Organization))
            fileHeader.Organization = savedFileHeaderMap[s_Organization];
         if (savedFileHeaderMap.ContainsKey(s_Authorization))
            fileHeader.Authorization = savedFileHeaderMap[s_Authorization];
         if (savedFileHeaderMap.ContainsKey(s_ApplicationName))
            fileHeader.ApplicationName = savedFileHeaderMap[s_ApplicationName];
         if (savedFileHeaderMap.ContainsKey(s_VersionNumber))
            fileHeader.VersionNumber = savedFileHeaderMap[s_VersionNumber];
         if (savedFileHeaderMap.ContainsKey(s_FileSchema))
            fileHeader.FileSchema = savedFileHeaderMap[s_FileSchema];

         return true;
      } 
   }   
}
