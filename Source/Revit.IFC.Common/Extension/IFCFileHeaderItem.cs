/*******************************************************************************
////////////////////////////////////////////////////////////////////////////////
 * IFCFileHeaderItem.cs
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
////////////////////////////////////////////////////////////////////////////////
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
using System.Text;
using System.ComponentModel;
using Autodesk.Revit.DB;
using System.Security.Policy;

namespace Revit.IFC.Common.Extensions
{
   public class IFCFileHeaderItem : INotifyPropertyChanged
   {
      public event PropertyChangedEventHandler PropertyChanged;

      /// <summary>
      /// Поле File Description Заголовка файла IFC.
      /// </summary>
      public IList<string> FileDescriptions { get; set; } = new List<string>();

      /// <summary>
      /// Добавить объект описания в список
      /// </summary>
      /// <param name="descriptionItem">строка, которая будет добавлена к описанию</param>
      public void AddDescription(string descriptionItem)
      {
         if (!string.IsNullOrEmpty(descriptionItem))
            FileDescriptions.Add(descriptionItem);
      }

      /// <summary>
      /// Удалите объект описания на основе ключевого слова
      /// </summary>
      /// <param name="keyword">ключевое слово, которое будет искаться</param>
      /// <returns>true, если ключевое слово найдено и успешно удалено, false, если не найдено</returns>
      public bool AddOrReplaceDescriptionItem(string newDescItem)
      {
         bool ret = false;
         string[] descItemToken = newDescItem.TrimStart().Split(':', ' ');
         if (descItemToken.Length >= 1 && !string.IsNullOrEmpty(descItemToken[0]))
         {
            string keyword = descItemToken[0];
            for (int idx = 0; idx < FileDescriptions.Count; ++idx)
            {
               string descriptionItem = FileDescriptions[idx];
               // Объекты являются тем же, никакая потребность сделать любую замену
               if (descriptionItem.Equals(newDescItem))
               {
                  ret = true;
                  break;
               }

               if (descriptionItem.TrimStart().StartsWith(keyword, StringComparison.InvariantCultureIgnoreCase))
               {
                  FileDescriptions.RemoveAt(idx);
                  FileDescriptions.Add(newDescItem);
                  ret = true;
                  break;
               }
            }
            if (!ret)
            {
               FileDescriptions.Add(newDescItem);
               ret = true;
            }
         }
         return ret;
      }

      ////////////////////////////////////////////////////
      /// <summary>
      /// Поле имени Исходного файла Заголовка файла IFC.
      /// </summary>
      ////////////////////////////////////////////////////
      private string sourceFileName;
      public string SourceFileName
      {
         get { return sourceFileName; }
         set
         {
            sourceFileName = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("SourceFileNameTextBox");
         }
      }

      /// <summary>
      /// true, если ключевое слово найдено и успешно удалено, false, если не найдено
      /// </summary>
      private string authorName;
      public string AuthorName
      {
         get { return authorName; }
         set
         {
            authorName = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("AuthorNameTextBox");
         }
      }

      /// <summary>
      /// Поле фамилии Автора Заголовка файла IFC.
      /// </summary>
      private string authorEmail;
      public string AuthorEmail
      {
         get { return authorEmail; }
         set
         {
            authorEmail = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("AuthorEmailTextBox");
         }
      }

      /// <summary>
      /// Поле Organization Заголовка файла IFC.
      /// </summary>
      private string organization;
      public string Organization
      {
         get { return organization; }
         set
         {
            organization = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("OrganizationTextBox");
         }
      }

      /// <summary>
      /// Поле Authorization Заголовка файла IFC.
      /// </summary>
      private string authorization;
      public string Authorization
      {
         get { return authorization; }
         set
         {
            authorization = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("AuthorizationTextBox");
         }
      }

      /// <summary>
      /// Поле Application Name Заголовка файла IFC.
      /// </summary>
      private string applicationName;
      public string ApplicationName
      {
         get { return applicationName; }
         set
         {
            applicationName = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("ApplicationNameTextBox");
         }
      }

      /// <summary>
      /// Поле Номера версии Заголовка файла IFC.
      /// </summary>
      private string versionNumber;
      public string VersionNumber
      {
         get { return versionNumber; }
         set
         {
            versionNumber = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("VersionNumberTextBox");
         }
      }

      /// <summary>
      /// Область Местоположения заголовка Файла IFC.
      /// </summary>
      private string fileSchema;
      public string FileSchema
      {
         get { return fileSchema; }
         set
         {
            fileSchema = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("fileSchemaTextBox");
         }
      }

      /// <summary>
      /// Проверьте, являются ли адреса тем же
      /// </summary>
      public Boolean isUnchanged(IFCFileHeaderItem headerToCheck)
      {
         if (this.Equals(headerToCheck))
            return true;
         return false;
      }

      /// <summary>
      /// Обработчик событий, когда свойство изменяется.
      /// </summary>
      /// <param name="name">название свойства.</param>
      protected void OnPropertyChanged(string name)
      {
         PropertyChangedEventHandler handler = PropertyChanged;
         if (handler != null)
         {
            handler(this, new PropertyChangedEventArgs(name));
         }
      }

      /// <summary>
      /// Клонировать информацию о Заголовке файла.
      /// </summary>
      /// <returns></returns>
      public IFCFileHeaderItem Clone()
      {
         return new IFCFileHeaderItem(this);
      }

      public IFCFileHeaderItem()
      {
      }

      public IFCFileHeaderItem(Document doc)
      {
         // Имя приложения и Число фиксируются для релиза программного обеспечения и не изменятся, поэтому они всегда вынуждаются набор здесь
         ApplicationName = doc.Application.VersionName;
         VersionNumber = doc.Application.VersionBuild;
      }


      //**************************************************************************************************
      ////////////////////////////////////////////////////////////////////////////////////////////////////
      /// <summary>
      /// Поле Comment файла IFC. (*** SGIMAN ***)
      /// </summary>
      private string sourceFileComment = "/* File Comment (sourceFileComment) */";
      public string FileComment
      {
         get { return sourceFileComment; }
         set
         {
            sourceFileComment = value;
            // Вызвать OnPropertyChanged каждый раз, когда свойство обновляется
            OnPropertyChanged("FileCommentTextBox");
         }
      }
      ////////////////////////////////////////////////////////////////////////////////////////////////////
      //**************************************************************************************************


      /// <summary>
      /// Фактическая копия/клон Информации заголовка.
      /// </summary>
      /// <param name="other">заголовок исходного файла для клонирования.</param>
      private IFCFileHeaderItem(IFCFileHeaderItem other)
      {
         FileComment = other.sourceFileComment;          // ***** SGIMAN *****

         FileDescriptions = other.FileDescriptions;
         SourceFileName = other.SourceFileName;
         AuthorName = other.AuthorName;
         AuthorEmail = other.AuthorEmail;
         Organization = other.Organization;
         Authorization = other.Authorization;
         ApplicationName = other.ApplicationName;
         VersionNumber = other.VersionNumber;
         FileSchema = other.FileSchema;
      }

   }
}