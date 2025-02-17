using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Runtime.ExceptionServices;

using CrystalDecisions.CrystalReports.Engine;
using CrystalDecisions.ReportAppServer.ClientDoc;
using CrystalDecisions.ReportAppServer.Controllers;
using CrystalDecisions.Shared;

using CRDataDefModel = CrystalDecisions.ReportAppServer.DataDefModel;
using CRReportDefModel = CrystalDecisions.ReportAppServer.ReportDefModel;

using OpenMcdf;

namespace RptToXml
{
	public partial class RptDefinitionWriter : IDisposable
	{
		private const FormatTypes ShowFormatTypes = FormatTypes.AreaFormat | FormatTypes.SectionFormat | FormatTypes.Color;

		private ReportDocument _report;
		private ISCDReportClientDocument _rcd;
		private CompoundFile _oleCompoundFile;

		private readonly bool _createdReport;

		public RptDefinitionWriter(string filename)
		{
			_createdReport = true;
			_report = new ReportDocument();
			_report.Load(filename, OpenReportMethod.OpenReportByTempCopy);
			_rcd = _report.ReportClientDocument;

			_oleCompoundFile = new CompoundFile(filename);

			Trace.WriteLine("Loaded report");
		}

		public RptDefinitionWriter(ReportDocument value)
		{
			_report = value;
			_rcd = _report.ReportClientDocument;
		}

		public void WriteToXml(System.IO.Stream output)
		{
			using (XmlTextWriter writer = new XmlTextWriter(output, Encoding.UTF8) { Formatting = Formatting.Indented })
			{
				WriteToXml(writer);
			}
		}

		public void WriteToXml(string targetXmlPath)
		{
			using (XmlTextWriter writer = new XmlTextWriter(targetXmlPath, Encoding.UTF8) {Formatting = Formatting.Indented })
			{
				WriteToXml(writer);
			}
		}

		public void WriteToXml(XmlWriter writer)
		{
			Trace.WriteLine("Writing to XML");

			writer.WriteStartDocument();
			ProcessReport(_report, writer);
			writer.WriteEndDocument();
			writer.Flush();
		}

		//This is a recursive method.  GetSubreports() calls it.
		private void ProcessReport(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("Report");

			writer.WriteAttributeString("Name", report.Name);
			Trace.WriteLine("Writing report " + report.Name);

			if (!report.IsSubreport)
			{
				Trace.WriteLine("Writing header info");

				writer.WriteAttributeString("FileName", report.FileName.Replace("rassdk://", ""));
				writer.WriteAttributeString("HasSavedData", report.HasSavedData.ToString());

				if (_oleCompoundFile != null)
				{
					writer.WriteStartElement("Embedinfo");
					_oleCompoundFile.RootStorage.VisitEntries(fileItem =>
					{
						if (fileItem.Name.Contains("Ole"))
						{
							writer.WriteStartElement("Embed");
							writer.WriteAttributeString("Name", fileItem.Name);

							var cfStream = fileItem as CFStream;
							if (cfStream != null)
							{
								var streamBytes = cfStream.GetData();

								writer.WriteAttributeString("Size", cfStream.Size.ToString("0"));

								using (var md5Provider = new MD5CryptoServiceProvider())
								{
									byte[] md5Hash = md5Provider.ComputeHash(streamBytes);
									writer.WriteAttributeString("MD5Hash", Convert.ToBase64String(md5Hash));
								}
							}
							writer.WriteEndElement();
						}
					}, true);
					writer.WriteEndElement();
				}

				GetSummaryinfo(report, writer);
				GetReportOptions(report, writer);
				GetPrintOptions(report, writer);
				GetSubreports(report, writer);	//recursion happens here.
			}

			GetDatabase(report, writer);
			GetDataDefinition(report, writer);
			GetCustomFunctions(report, writer);
			GetReportDefinition(report, writer);

			writer.WriteEndElement();
		}

		private static void GetSummaryinfo(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("Summaryinfo");

			writer.WriteAttributeString("KeywordsinReport", report.SummaryInfo.KeywordsInReport);
			writer.WriteAttributeString("ReportAuthor", report.SummaryInfo.ReportAuthor);
			writer.WriteAttributeString("ReportComments", report.SummaryInfo.ReportComments);
			writer.WriteAttributeString("ReportSubject", report.SummaryInfo.ReportSubject);
			writer.WriteAttributeString("ReportTitle", report.SummaryInfo.ReportTitle);

			writer.WriteEndElement();
		}

		private static void GetReportOptions(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("ReportOptions");

			writer.WriteAttributeString("EnableSaveDataWithReport", report.ReportOptions.EnableSaveDataWithReport.ToString());
			writer.WriteAttributeString("EnableSavePreviewPicture", report.ReportOptions.EnableSavePreviewPicture.ToString());
			writer.WriteAttributeString("EnableSaveSummariesWithReport", report.ReportOptions.EnableSaveSummariesWithReport.ToString());
			writer.WriteAttributeString("EnableUseDummyData", report.ReportOptions.EnableUseDummyData.ToString());
			writer.WriteAttributeString("initialDataContext", report.ReportOptions.InitialDataContext);
			writer.WriteAttributeString("initialReportPartName", report.ReportOptions.InitialReportPartName);

			writer.WriteEndElement();
		}

		private void GetPrintOptions(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("PrintOptions");

			writer.WriteAttributeString("PageContentHeight", report.PrintOptions.PageContentHeight.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("PageContentWidth", report.PrintOptions.PageContentWidth.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("PaperOrientation", report.PrintOptions.PaperOrientation.ToString());
			writer.WriteAttributeString("PaperSize", report.PrintOptions.PaperSize.ToString());
			writer.WriteAttributeString("PaperSource", report.PrintOptions.PaperSource.ToString());
			writer.WriteAttributeString("PrinterDuplex", report.PrintOptions.PrinterDuplex.ToString());
			writer.WriteAttributeString("PrinterName", report.PrintOptions.PrinterName);

			writer.WriteStartElement("PageMargins");

			writer.WriteAttributeString("bottomMargin", report.PrintOptions.PageMargins.bottomMargin.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("leftMargin", report.PrintOptions.PageMargins.leftMargin.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("rightMargin", report.PrintOptions.PageMargins.rightMargin.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("topMargin", report.PrintOptions.PageMargins.topMargin.ToString(CultureInfo.InvariantCulture));

			writer.WriteEndElement();

			CRReportDefModel.PrintOptions rdmPrintOptions = GetRASRDMPrintOptionsObject(report.Name, report);
			if (rdmPrintOptions != null)
				GetPageMarginConditionFormulas(rdmPrintOptions, writer);

			writer.WriteEndElement();
		}

		[HandleProcessCorruptedStateExceptionsAttribute]
		private void GetSubreports(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("SubReports");

			try { 
			foreach (ReportDocument subreport in report.Subreports)
				ProcessReport(subreport, writer);
			}
			catch (Exception e)
			{
				Console.WriteLine($"Error loading subpreport, {e}");
			}
			writer.WriteEndElement();
		}

		private void GetDatabase(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("Database");

			GetTableLinks(report, writer);
			if (!report.IsSubreport)
			{
				var reportClientDocument = report.ReportClientDocument;
				GetReportClientTables(reportClientDocument, writer);
			}
			else
			{
				var subrptClientDoc = _report.ReportClientDocument.SubreportController.GetSubreport(report.Name);
				GetSubreportClientTables(subrptClientDoc, writer);
			}

			writer.WriteEndElement();
		}

		private static void GetTableLinks(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("TableLinks");

			foreach (TableLink tl in report.Database.Links)
			{
				writer.WriteStartElement("TableLink");
				writer.WriteAttributeString("JoinType", tl.JoinType.ToString());

				writer.WriteStartElement("SourceFields");
				foreach (FieldDefinition fd in tl.SourceFields)
					GetFieldDefinition(fd, writer);
				writer.WriteEndElement();

				writer.WriteStartElement("DestinationFields");
				foreach (FieldDefinition fd in tl.DestinationFields)
					GetFieldDefinition(fd, writer);
				writer.WriteEndElement();

				writer.WriteEndElement();
			}

			writer.WriteEndElement();
		}

		private void GetCustomFunctions(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("CustomFunctions");

			CRDataDefModel.CustomFunctions funcs;
			if (!report.IsSubreport)
			{
				funcs = report.ReportClientDocument.CustomFunctionController.GetCustomFunctions();
			}
			else
			{
				var subrptClientDoc = _report.ReportClientDocument.SubreportController.GetSubreport(report.Name);
				//funcs = subrptClientDoc.CustomFunctionController.GetCustomFunctions();
				funcs = null;
			}

			if (funcs != null)
			{
				foreach (CRDataDefModel.CustomFunction func in funcs)
				{
					writer.WriteStartElement("CustomFunction");
					writer.WriteAttributeString("Name", func.Name);
					writer.WriteAttributeString("Syntax", func.Syntax.ToString());
					writer.WriteElementString("Text", func.Text); // an element so line breaks are literal

					writer.WriteEndElement();
				}
			}

			writer.WriteEndElement();
		}

		private void GetReportClientTables(ISCDReportClientDocument reportClientDocument, XmlWriter writer)
		{
			writer.WriteStartElement("Tables");

			foreach (CrystalDecisions.ReportAppServer.DataDefModel.Table table in reportClientDocument.DatabaseController.Database.Tables)
			{
				GetTable(table, writer);
			}

			writer.WriteEndElement();
		}
		private void GetSubreportClientTables(SubreportClientDocument subrptClientDocument, XmlWriter writer)
		{
			writer.WriteStartElement("Tables");

			foreach (CrystalDecisions.ReportAppServer.DataDefModel.Table table in subrptClientDocument.DatabaseController.Database.Tables)
			{
				GetTable(table, writer);
			}

			writer.WriteEndElement();
		}

		private void GetTable(CrystalDecisions.ReportAppServer.DataDefModel.Table table, XmlWriter writer)
		{
			writer.WriteStartElement("Table");

			writer.WriteAttributeString("Alias", table.Alias);
			writer.WriteAttributeString("ClassName", table.ClassName);
			writer.WriteAttributeString("Name", table.Name);

			writer.WriteStartElement("ConnectionInfo");
			foreach (string propertyId in table.ConnectionInfo.Attributes.PropertyIDs)
			{
				// make attribute name safe for XML
				string attributeName = propertyId.Replace(" ", "_");

				writer.WriteAttributeString(attributeName, table.ConnectionInfo.Attributes[propertyId].ToString());
			}

			writer.WriteAttributeString("UserName", table.ConnectionInfo.UserName);
			writer.WriteAttributeString("Password", table.ConnectionInfo.Password);
			writer.WriteEndElement();

			var commandTable = table as CRDataDefModel.CommandTable;
			if (commandTable != null)
			{
				var cmdTable = commandTable;
				writer.WriteStartElement("Command");
				writer.WriteString(cmdTable.CommandText);
				writer.WriteEndElement();
			}

			writer.WriteStartElement("Fields");

			foreach (CrystalDecisions.ReportAppServer.DataDefModel.Field fd in table.DataFields)
			{
				GetFieldDefinition(fd, writer);
			}

			writer.WriteEndElement();

			writer.WriteEndElement();
		}

		private static void GetFieldDefinition(FieldDefinition fd, XmlWriter writer)
		{
			writer.WriteStartElement("Field");

			writer.WriteAttributeString("FormulaName", fd.FormulaName);
			writer.WriteAttributeString("Kind", fd.Kind.ToString());
			writer.WriteAttributeString("Name", fd.Name);
			writer.WriteAttributeString("NumberOfBytes", fd.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("ValueType", fd.ValueType.ToString());

			writer.WriteEndElement();
		}

		private static void GetFieldDefinition(CrystalDecisions.ReportAppServer.DataDefModel.Field fd, XmlWriter writer)
		{
			writer.WriteStartElement("Field");

			writer.WriteAttributeString("Description", fd.Description);
			writer.WriteAttributeString("FormulaForm", fd.FormulaForm);
			writer.WriteAttributeString("HeadingText", fd.HeadingText);
			writer.WriteAttributeString("IsRecurring", fd.IsRecurring.ToString());
			writer.WriteAttributeString("Kind", fd.Kind.ToString());
			writer.WriteAttributeString("Length", fd.Length.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("LongName", fd.LongName);
			writer.WriteAttributeString("Name", fd.Name);
			writer.WriteAttributeString("ShortName", fd.ShortName);
			writer.WriteAttributeString("Type", fd.Type.ToString());
			writer.WriteAttributeString("UseCount", fd.UseCount.ToString(CultureInfo.InvariantCulture));

			writer.WriteEndElement();
		}

		private void GetDataDefinition(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("DataDefinition");

			writer.WriteElementString("GroupSelectionFormula", report.DataDefinition.GroupSelectionFormula);
			writer.WriteElementString("RecordSelectionFormula", report.DataDefinition.RecordSelectionFormula);

			writer.WriteStartElement("Groups");
			foreach (Group group in report.DataDefinition.Groups)
			{
				writer.WriteStartElement("Group");
				writer.WriteAttributeString("ConditionField", group.ConditionField.FormulaName);

				writer.WriteEndElement();

			}
			writer.WriteEndElement();

			writer.WriteStartElement("SortFields");
			foreach (SortField sortField in report.DataDefinition.SortFields)
			{
				writer.WriteStartElement("SortField");

				writer.WriteAttributeString("Field", sortField.Field.FormulaName);
				try
				{
					string sortDirection = sortField.SortDirection.ToString();
					writer.WriteAttributeString("SortDirection", sortDirection);
				}
				catch (NotSupportedException)
				{ }
				writer.WriteAttributeString("SortType", sortField.SortType.ToString());

				writer.WriteEndElement();
			}
			writer.WriteEndElement();

			writer.WriteStartElement("FormulaFieldDefinitions");
			foreach (var field in report.DataDefinition.FormulaFields.OfType<FieldDefinition>().OrderBy(field => field.FormulaName))
				GetFieldObject(field, report, writer);
			writer.WriteEndElement();

			writer.WriteStartElement("GroupNameFieldDefinitions");
			foreach (var field in report.DataDefinition.GroupNameFields)
				GetFieldObject(field, report, writer);
			writer.WriteEndElement();

			writer.WriteStartElement("ParameterFieldDefinitions");
			try { 
				foreach (var field in report.DataDefinition.ParameterFields)
					GetFieldObject(field, report, writer);
			} catch( Exception e)
			{
				Console.WriteLine($"Error processing ParameterFieldDefinitions, {e}");
			}
			writer.WriteEndElement();

			writer.WriteStartElement("RunningTotalFieldDefinitions");
			foreach (var field in report.DataDefinition.RunningTotalFields)
				GetFieldObject(field, report, writer);
			writer.WriteEndElement();

			writer.WriteStartElement("SQLExpressionFields");
			foreach (var field in report.DataDefinition.SQLExpressionFields)
				GetFieldObject(field, report, writer);
			writer.WriteEndElement();

			writer.WriteStartElement("SummaryFields");
			foreach (var field in report.DataDefinition.SummaryFields)
				GetFieldObject(field, report, writer);
			writer.WriteEndElement();

			writer.WriteEndElement();
		}

		[HandleProcessCorruptedStateExceptionsAttribute]
		private void GetFieldObject(Object fo, ReportDocument report, XmlWriter writer)
		{
			if (fo is DatabaseFieldDefinition)
			{
				var df = (DatabaseFieldDefinition)fo;

				writer.WriteStartElement("DatabaseFieldDefinition");

				writer.WriteAttributeString("FormulaName", df.FormulaName);
				writer.WriteAttributeString("Kind", df.Kind.ToString());
				writer.WriteAttributeString("Name", df.Name);
				writer.WriteAttributeString("NumberOfBytes", df.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("TableName", df.TableName);
				writer.WriteAttributeString("ValueType", df.ValueType.ToString());

			}
			else if (fo is FormulaFieldDefinition)
			{
				var ff = (FormulaFieldDefinition)fo;

				writer.WriteStartElement("FormulaFieldDefinition");

				writer.WriteAttributeString("FormulaName", ff.FormulaName);
				writer.WriteAttributeString("Kind", ff.Kind.ToString());
				writer.WriteAttributeString("Name", ff.Name);
				writer.WriteAttributeString("NumberOfBytes", ff.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("ValueType", ff.ValueType.ToString());
				writer.WriteString(ff.Text);

			}
			else if (fo is GroupNameFieldDefinition)
			{
				var gnf = (GroupNameFieldDefinition)fo;

				writer.WriteStartElement("GroupNameFieldDefinition");
				try 
				{
					writer.WriteAttributeString("FormulaName", gnf.FormulaName);
					writer.WriteAttributeString("Group", gnf.Group.ToString());
					writer.WriteAttributeString("GroupNameFieldName", gnf.GroupNameFieldName);
					writer.WriteAttributeString("Kind", gnf.Kind.ToString());
					writer.WriteAttributeString("Name", gnf.Name);
					writer.WriteAttributeString("NumberOfBytes", gnf.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
					writer.WriteAttributeString("ValueType", gnf.ValueType.ToString());
				}
				catch( Exception e)
				{
					Console.WriteLine($"Error loading formula for group '{gnf.GroupNameFieldName}', {e}");
				}
			}
			else if (fo is ParameterFieldDefinition)
			{
				var pf = (ParameterFieldDefinition)fo;

				// if it is a linked parameter, it is passed into a subreport. Just record the actual linkage in the main report.
				// The parameter will be reported in full when the subreport is exported.  
				var parameterIsLinked = (!report.IsSubreport && pf.IsLinked());

				writer.WriteStartElement("ParameterFieldDefinition");

				if (parameterIsLinked)
				{
					writer.WriteAttributeString("Name", pf.Name);
					writer.WriteAttributeString("IsLinkedToSubreport", pf.IsLinked().ToString());
					writer.WriteAttributeString("ReportName", pf.ReportName);
				}
				else
				{
					var ddm_pf = GetRASDDMParameterFieldObject(pf.Name, report);

					writer.WriteAttributeString("AllowCustomCurrentValues", (ddm_pf == null ? false : ddm_pf.AllowCustomCurrentValues).ToString());
					writer.WriteAttributeString("EditMask", pf.EditMask);
					writer.WriteAttributeString("EnableAllowEditingDefaultValue", pf.EnableAllowEditingDefaultValue.ToString());
					writer.WriteAttributeString("EnableAllowMultipleValue", pf.EnableAllowMultipleValue.ToString());
					writer.WriteAttributeString("EnableNullValue", pf.EnableNullValue.ToString());
					writer.WriteAttributeString("FormulaName", pf.FormulaName);
					writer.WriteAttributeString("HasCurrentValue", pf.HasCurrentValue.ToString());
					writer.WriteAttributeString("IsOptionalPrompt", pf.IsOptionalPrompt.ToString());
					writer.WriteAttributeString("Kind", pf.Kind.ToString());
					//writer.WriteAttributeString("MaximumValue", (string) pf.MaximumValue);
					//writer.WriteAttributeString("MinimumValue", (string) pf.MinimumValue);
					writer.WriteAttributeString("Name", pf.Name);
					writer.WriteAttributeString("NumberOfBytes", pf.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
					writer.WriteAttributeString("ParameterFieldName", pf.ParameterFieldName);
					writer.WriteAttributeString("ParameterFieldUsage", pf.ParameterFieldUsage2.ToString());
					writer.WriteAttributeString("ParameterType", pf.ParameterType.ToString());
					writer.WriteAttributeString("ParameterValueKind", pf.ParameterValueKind.ToString());
					writer.WriteAttributeString("PromptText", pf.PromptText);
					writer.WriteAttributeString("ReportName", pf.ReportName);
					writer.WriteAttributeString("ValueType", pf.ValueType.ToString());

					writer.WriteStartElement("ParameterDefaultValues");
					if (pf.DefaultValues.Count > 0)
					{
						foreach (ParameterValue pv in pf.DefaultValues)
						{
							writer.WriteStartElement("ParameterDefaultValue");
							writer.WriteAttributeString("Description", pv.Description);
							// TODO: document dynamic parameters
							if (!pv.IsRange)
							{
								ParameterDiscreteValue pdv = (ParameterDiscreteValue)pv;
								writer.WriteAttributeString("Value", pdv.Value.ToString());
							}
							writer.WriteEndElement();
						}
					}
					writer.WriteEndElement();

					writer.WriteStartElement("ParameterInitialValues");
					if (ddm_pf != null)
					{
						if (ddm_pf.InitialValues.Count > 0)
						{
							foreach (CRDataDefModel.ParameterFieldValue pv in ddm_pf.InitialValues)
							{
								writer.WriteStartElement("ParameterInitialValue");
								CRDataDefModel.ParameterFieldDiscreteValue pdv = (CRDataDefModel.ParameterFieldDiscreteValue)pv;
								writer.WriteAttributeString("Value", pdv.Value.ToString());
								writer.WriteEndElement();
							}
						}
					}
					writer.WriteEndElement();

					writer.WriteStartElement("ParameterCurrentValues");
					if (pf.CurrentValues.Count > 0)
					{
						foreach (ParameterValue pv in pf.CurrentValues)
						{
							writer.WriteStartElement("ParameterCurrentValue");
							writer.WriteAttributeString("Description", pv.Description);
							// TODO: document dynamic parameters
							if (!pv.IsRange)
							{
								ParameterDiscreteValue pdv = (ParameterDiscreteValue)pv;
								writer.WriteAttributeString("Value", pdv.Value.ToString());
							}
							writer.WriteEndElement();
						}
					}
					writer.WriteEndElement();
				}

			}
			else if (fo is RunningTotalFieldDefinition)
			{
				var rtf = (RunningTotalFieldDefinition)fo;

				writer.WriteStartElement("RunningTotalFieldDefinition");
				//writer.WriteAttributeString("EvaluationConditionType", rtf.EvaluationCondition);
				writer.WriteAttributeString("EvaluationConditionType", rtf.EvaluationConditionType.ToString());
				writer.WriteAttributeString("FormulaName", rtf.FormulaName);
				if (rtf.Group != null) writer.WriteAttributeString("Group", rtf.Group.ToString());
				writer.WriteAttributeString("Kind", rtf.Kind.ToString());
				writer.WriteAttributeString("Name", rtf.Name);
				writer.WriteAttributeString("NumberOfBytes", rtf.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("Operation", rtf.Operation.ToString());
				writer.WriteAttributeString("OperationParameter", rtf.OperationParameter.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("ResetConditionType", rtf.ResetConditionType.ToString());

				if (rtf.SecondarySummarizedField != null)
					writer.WriteAttributeString("SecondarySummarizedField", rtf.SecondarySummarizedField.FormulaName);

				writer.WriteAttributeString("SummarizedField", rtf.SummarizedField.FormulaName);
				writer.WriteAttributeString("ValueType", rtf.ValueType.ToString());

			}
			else if (fo is SpecialVarFieldDefinition)
			{
				writer.WriteStartElement("SpecialVarFieldDefinition");
				var svf = (SpecialVarFieldDefinition)fo;
				writer.WriteAttributeString("FormulaName", svf.FormulaName);
				writer.WriteAttributeString("Kind", svf.Kind.ToString());
				writer.WriteAttributeString("Name", svf.Name);
				writer.WriteAttributeString("NumberOfBytes", svf.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("SpecialVarType", svf.SpecialVarType.ToString());
				writer.WriteAttributeString("ValueType", svf.ValueType.ToString());

			}
			else if (fo is SQLExpressionFieldDefinition)
			{
				writer.WriteStartElement("SQLExpressionFieldDefinition");
				var sef = (SQLExpressionFieldDefinition)fo;

				writer.WriteAttributeString("FormulaName", sef.FormulaName);
				writer.WriteAttributeString("Kind", sef.Kind.ToString());
				writer.WriteAttributeString("Name", sef.Name);
				writer.WriteAttributeString("NumberOfBytes", sef.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("Text", sef.Text);
				writer.WriteAttributeString("ValueType", sef.ValueType.ToString());

			}
			else if (fo is SummaryFieldDefinition)
			{
				writer.WriteStartElement("SummaryFieldDefinition");

				var sf = (SummaryFieldDefinition)fo;

				writer.WriteAttributeString("FormulaName", sf.FormulaName);

				if (sf.Group != null)
					writer.WriteAttributeString("Group", sf.Group.ToString());

				writer.WriteAttributeString("Kind", sf.Kind.ToString());
				writer.WriteAttributeString("Name", sf.Name);
				writer.WriteAttributeString("NumberOfBytes", sf.NumberOfBytes.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("Operation", sf.Operation.ToString());
				writer.WriteAttributeString("OperationParameter", sf.OperationParameter.ToString(CultureInfo.InvariantCulture));
				if (sf.SecondarySummarizedField != null) writer.WriteAttributeString("SecondarySummarizedField", sf.SecondarySummarizedField.ToString());
				writer.WriteAttributeString("SummarizedField", sf.SummarizedField.ToString());
				writer.WriteAttributeString("ValueType", sf.ValueType.ToString());

			}
			writer.WriteEndElement();
		}

		private CRDataDefModel.ParameterField GetRASDDMParameterFieldObject(string fieldName, ReportDocument report)
		{
			CRDataDefModel.ParameterField rdm;
			if (report.IsSubreport)
			{
				var subrptClientDoc = _report.ReportClientDocument.SubreportController.GetSubreport(report.Name);
				rdm = subrptClientDoc.DataDefController.DataDefinition.ParameterFields.FindField(fieldName,
					CRDataDefModel.CrFieldDisplayNameTypeEnum.crFieldDisplayNameName) as CRDataDefModel.ParameterField;
			}
			else
			{
				rdm = _rcd.DataDefController.DataDefinition.ParameterFields.FindField(fieldName,
					CRDataDefModel.CrFieldDisplayNameTypeEnum.crFieldDisplayNameName) as CRDataDefModel.ParameterField;
			}
			return rdm;
		}

		private void GetAreaFormat(Area area, ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("AreaFormat");

			writer.WriteAttributeString("EnableHideForDrillDown", area.AreaFormat.EnableHideForDrillDown.ToString());
			writer.WriteAttributeString("EnableKeepTogether", area.AreaFormat.EnableKeepTogether.ToString());
			writer.WriteAttributeString("EnableNewPageAfter", area.AreaFormat.EnableNewPageAfter.ToString());
			writer.WriteAttributeString("EnableNewPageBefore", area.AreaFormat.EnableNewPageBefore.ToString());
			writer.WriteAttributeString("EnablePrintAtBottomOfPage", area.AreaFormat.EnablePrintAtBottomOfPage.ToString());
			writer.WriteAttributeString("EnableResetPageNumberAfter", area.AreaFormat.EnableResetPageNumberAfter.ToString());
			writer.WriteAttributeString("EnableSuppress", area.AreaFormat.EnableSuppress.ToString());

			if (area.Kind == AreaSectionKind.GroupHeader)
			{
				GroupAreaFormat gaf = (GroupAreaFormat)area.AreaFormat;
				writer.WriteStartElement("GroupAreaFormat");
				writer.WriteAttributeString("EnableKeepGroupTogether", gaf.EnableKeepGroupTogether.ToString());
				writer.WriteAttributeString("EnableRepeatGroupHeader", gaf.EnableRepeatGroupHeader.ToString());
				writer.WriteAttributeString("VisibleGroupNumberPerPage", gaf.VisibleGroupNumberPerPage.ToString());
				writer.WriteEndElement();
			}	
			writer.WriteEndElement();
						
		}

		private void GetBorderFormat(ReportObject ro, ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("Border");

			var border = ro.Border;
			writer.WriteAttributeString("BottomLineStyle", border.BottomLineStyle.ToString());
			writer.WriteAttributeString("HasDropShadow", border.HasDropShadow.ToString());
			writer.WriteAttributeString("LeftLineStyle", border.LeftLineStyle.ToString());
			writer.WriteAttributeString("RightLineStyle", border.RightLineStyle.ToString());
			writer.WriteAttributeString("TopLineStyle", border.TopLineStyle.ToString());

			CRReportDefModel.ISCRReportObject rdm_ro = GetRASRDMReportObject(ro.Name, report);
			if (rdm_ro != null)
				GetBorderConditionFormulas(rdm_ro, writer);

			if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
				GetColorFormat(border.BackgroundColor, writer, "BackgroundColor");
			if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
				GetColorFormat(border.BorderColor, writer, "BorderColor");

			writer.WriteEndElement();
		}

		private static void GetColorFormat(Color color, XmlWriter writer, String elementName = "Color")
		{
			writer.WriteStartElement(elementName);

			writer.WriteAttributeString("Name", color.Name);
			writer.WriteAttributeString("A", color.A.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("R", color.R.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("G", color.G.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("B", color.B.ToString(CultureInfo.InvariantCulture));

			writer.WriteEndElement();
		}

		private void GetFontFormat(Font font, ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("Font");

			writer.WriteAttributeString("Bold", font.Bold.ToString());
			writer.WriteAttributeString("FontFamily", font.FontFamily.Name);
			writer.WriteAttributeString("GdiCharSet", font.GdiCharSet.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("GdiVerticalFont", font.GdiVerticalFont.ToString());
			writer.WriteAttributeString("Height", font.Height.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("IsSystemFont", font.IsSystemFont.ToString());
			writer.WriteAttributeString("Italic", font.Italic.ToString());
			writer.WriteAttributeString("Name", font.Name);
			writer.WriteAttributeString("OriginalFontName", font.OriginalFontName);
			writer.WriteAttributeString("Size", font.Size.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("SizeinPoints", font.SizeInPoints.ToString(CultureInfo.InvariantCulture));
			writer.WriteAttributeString("Strikeout", font.Strikeout.ToString());
			writer.WriteAttributeString("Style", font.Style.ToString());
			writer.WriteAttributeString("SystemFontName", font.SystemFontName);
			writer.WriteAttributeString("Underline", font.Underline.ToString());
			writer.WriteAttributeString("Unit", font.Unit.ToString());

			writer.WriteEndElement();
		}

		private void GetObjectFormat(ReportObject ro, ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("ObjectFormat");


			writer.WriteAttributeString("CssClass", ro.ObjectFormat.CssClass);
			writer.WriteAttributeString("EnableCanGrow", ro.ObjectFormat.EnableCanGrow.ToString());
			writer.WriteAttributeString("EnableCloseAtPageBreak", ro.ObjectFormat.EnableCloseAtPageBreak.ToString());
			writer.WriteAttributeString("EnableKeepTogether", ro.ObjectFormat.EnableKeepTogether.ToString());
			writer.WriteAttributeString("EnableSuppress", ro.ObjectFormat.EnableSuppress.ToString());
			writer.WriteAttributeString("HorizontalAlignment", ro.ObjectFormat.HorizontalAlignment.ToString());



			writer.WriteEndElement();
		}

		private void GetSectionFormat(Section section, ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("SectionFormat");

			writer.WriteAttributeString("CssClass", section.SectionFormat.CssClass);
			writer.WriteAttributeString("EnableKeepTogether", section.SectionFormat.EnableKeepTogether.ToString());
			writer.WriteAttributeString("EnableNewPageAfter", section.SectionFormat.EnableNewPageAfter.ToString());
			writer.WriteAttributeString("EnableNewPageBefore", section.SectionFormat.EnableNewPageBefore.ToString());
			writer.WriteAttributeString("EnablePrintAtBottomOfPage", section.SectionFormat.EnablePrintAtBottomOfPage.ToString());
			writer.WriteAttributeString("EnableResetPageNumberAfter", section.SectionFormat.EnableResetPageNumberAfter.ToString());
			writer.WriteAttributeString("EnableSuppress", section.SectionFormat.EnableSuppress.ToString());
			writer.WriteAttributeString("EnableSuppressIfBlank", section.SectionFormat.EnableSuppressIfBlank.ToString());
			writer.WriteAttributeString("EnableUnderlaySection", section.SectionFormat.EnableUnderlaySection.ToString());

			CRReportDefModel.Section rdm_ro = GetRASRDMSectionObjectFromCRENGSectionObject(section.Name, report);
			if (rdm_ro != null)
				GetSectionAreaFormatConditionFormulas(rdm_ro, writer);


			if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
				GetColorFormat(section.SectionFormat.BackgroundColor, writer, "BackgroundColor");

			writer.WriteEndElement();
		}

		private void GetReportDefinition(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("ReportDefinition");

			GetAreas(report, writer);

			writer.WriteEndElement();
		}

		private void GetAreas(ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("Areas");

			foreach (Area area in report.ReportDefinition.Areas)
			{
				writer.WriteStartElement("Area");

				writer.WriteAttributeString("Kind", area.Kind.ToString());
				writer.WriteAttributeString("Name", area.Name);

				if ((ShowFormatTypes & FormatTypes.AreaFormat) == FormatTypes.AreaFormat)
					GetAreaFormat(area, report, writer);

				GetSections(area, report, writer);

				writer.WriteEndElement();
			}

			writer.WriteEndElement();
		}

		private void GetSections(Area area, ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("Sections");

			foreach (Section section in area.Sections)
			{
				writer.WriteStartElement("Section");

				writer.WriteAttributeString("Height", section.Height.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("Kind", section.Kind.ToString());
				writer.WriteAttributeString("Name", section.Name);

				if ((ShowFormatTypes & FormatTypes.SectionFormat) == FormatTypes.SectionFormat)
					GetSectionFormat(section, report, writer);

				GetReportObjects(section, report, writer);

				writer.WriteEndElement();
			}

			writer.WriteEndElement();
		}

		private void GetReportObjects(Section section, ReportDocument report, XmlWriter writer)
		{
			writer.WriteStartElement("ReportObjects");

			foreach (ReportObject reportObject in section.ReportObjects)
			{
				writer.WriteStartElement(reportObject.GetType().Name);

				CRReportDefModel.ISCRReportObject rasrdm_ro = GetRASRDMReportObject(reportObject.Name, report);

				writer.WriteAttributeString("Name", reportObject.Name);
				writer.WriteAttributeString("Kind", reportObject.Kind.ToString());

				writer.WriteAttributeString("Top", reportObject.Top.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("Left", reportObject.Left.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("Width", reportObject.Width.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("Height", reportObject.Height.ToString(CultureInfo.InvariantCulture));

				if (reportObject is BoxObject)
				{
					var bo = (BoxObject)reportObject;
					writer.WriteAttributeString("Bottom", bo.Bottom.ToString(CultureInfo.InvariantCulture));
					writer.WriteAttributeString("EnableExtendToBottomOfSection", bo.EnableExtendToBottomOfSection.ToString());
					writer.WriteAttributeString("EndSectionName", bo.EndSectionName);
					writer.WriteAttributeString("LineStyle", bo.LineStyle.ToString());
					writer.WriteAttributeString("LineThickness", bo.LineThickness.ToString(CultureInfo.InvariantCulture));
					writer.WriteAttributeString("Right", bo.Right.ToString(CultureInfo.InvariantCulture));
					if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
						GetColorFormat(bo.LineColor, writer, "LineColor");
				}
				else if (reportObject is DrawingObject)
				{
					var dobj = (DrawingObject)reportObject;
					writer.WriteAttributeString("Bottom", dobj.Bottom.ToString(CultureInfo.InvariantCulture));
					writer.WriteAttributeString("EnableExtendToBottomOfSection", dobj.EnableExtendToBottomOfSection.ToString());
					writer.WriteAttributeString("EndSectionName", dobj.EndSectionName);
					writer.WriteAttributeString("LineStyle", dobj.LineStyle.ToString());
					writer.WriteAttributeString("LineThickness", dobj.LineThickness.ToString(CultureInfo.InvariantCulture));
					writer.WriteAttributeString("Right", dobj.Right.ToString(CultureInfo.InvariantCulture));
					if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
						GetColorFormat(dobj.LineColor, writer, "LineColor");
				}
				else if (reportObject is FieldHeadingObject)
				{
					var fh = (FieldHeadingObject)reportObject;
					var rasrdm_fh = (CRReportDefModel.FieldHeadingObject)rasrdm_ro;
					writer.WriteAttributeString("FieldObjectName", fh.FieldObjectName);
					writer.WriteAttributeString("MaxNumberOfLines", rasrdm_fh.MaxNumberOfLines.ToString());
					writer.WriteElementString("Text", fh.Text);

					if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
						GetColorFormat(fh.Color, writer);

					if ((ShowFormatTypes & FormatTypes.Font) == FormatTypes.Font)
					{
						GetFontFormat(fh.Font, report, writer);
						GetFontColorConditionFormulas(rasrdm_fh.FontColor, writer);
					}
				}
				else if (reportObject is FieldObject)
				{
					var fo = (FieldObject)reportObject;
					var rasrdm_fo = (CRReportDefModel.FieldObject)rasrdm_ro;
							
					if (fo.DataSource != null)
						writer.WriteAttributeString("DataSource", fo.DataSource.FormulaName);

					if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
						GetColorFormat(fo.Color, writer);

					if ((ShowFormatTypes & FormatTypes.Font) == FormatTypes.Font)
					{
						GetFontFormat(fo.Font, report, writer);
						GetFontColorConditionFormulas(rasrdm_fo.FontColor, writer);
					}

				}
				else if (reportObject is TextObject)
				{
					var tobj = (TextObject)reportObject;
					var rasrdm_tobj = (CRReportDefModel.TextObject)rasrdm_ro;

					writer.WriteAttributeString("MaxNumberOfLines", rasrdm_tobj.MaxNumberOfLines.ToString());
					writer.WriteElementString("Text", tobj.Text);

					if ((ShowFormatTypes & FormatTypes.Color) == FormatTypes.Color)
						GetColorFormat(tobj.Color, writer);
					if ((ShowFormatTypes & FormatTypes.Font) == FormatTypes.Font)
					{
						GetFontFormat(tobj.Font, report, writer);
						GetFontColorConditionFormulas(rasrdm_tobj.FontColor, writer);
					}
				}

				if ((ShowFormatTypes & FormatTypes.Border) == FormatTypes.Border)
					GetBorderFormat(reportObject, report, writer);

				if ((ShowFormatTypes & FormatTypes.ObjectFormat) == FormatTypes.ObjectFormat)
					GetObjectFormat(reportObject, report, writer);


				if (rasrdm_ro != null)
					GetObjectFormatConditionFormulas(rasrdm_ro, writer);

				writer.WriteEndElement();
			}

			writer.WriteEndElement();
		}

		// pretty much straight from api docs
		private CommonFieldFormat GetCommonFieldFormat(string reportObjectName, ReportDocument report)
		{
			FieldObject field = report.ReportDefinition.ReportObjects[reportObjectName] as FieldObject;
			if (field != null)
			{
				return field.FieldFormat.CommonFormat;
			}
			return null;
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (_report != null && _createdReport)
					_report.Dispose();

				_report = null;
				_rcd = null;

				if (_oleCompoundFile != null)
				{
					((IDisposable)_oleCompoundFile).Dispose();
					_oleCompoundFile = null;
				}
			}
		}
	}
}
