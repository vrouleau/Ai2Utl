﻿
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Xml
Imports System.Xml.Serialization
Imports Newtonsoft.Json

Public Class Form1
    Dim aircraftMap As New Dictionary(Of String, HashSet(Of String))
    Dim aircraftNotUsedSet As New SortedSet(Of String)
    Dim totalCount As Integer = 0
    Dim utlCodeMap As New Dictionary(Of String, String())
    Dim utlAigaimCodeMap As New Dictionary(Of String, String())
    Dim aigaimCodeToAiCodeMap As New Dictionary(Of String, String)
    Dim settings As Settings
    Dim searchCodes() As SearchCode
    Dim logWriter As System.IO.StreamWriter
    Dim logWriterFileName As String = "dhq.ai2utl.log.txt"
    Dim repaintsInformation As repaints_information
    Dim totalReplaced As Integer
    Dim totalAiModels As Integer
    Dim replacementFile As String
    Dim repaintFile As String
    Dim repaintsSer As XmlSerializer = New XmlSerializer(GetType(repaints_information))

    Public Sub New()

        InitializeComponent()

        ReadSettings()
    End Sub

    Private Sub ReadSettings()
        Try
            Dim settingsjson As String = File.ReadAllText("dhq.ai2utl.settings.json")
            settings = JsonConvert.DeserializeObject(Of Settings)(settingsjson)
            replacementFile = settings.utl.path + "\dhq.ai2utl.replacement.result.xml"
            ReplacementFileLabel.Text = replacementFile

            repaintFile = settings.utl.path + "\" + settings.utl.repaintFileName
            RepaintFileLabel.Text = repaintFile
            If settings.addOnlyMissing = False Then
                ReplaceAllLabel.Text = "REPLACE ALL"
            Else
                ReplaceAllLabel.Text = "ONLY MISSING"
            End If
            Select Case settings.programMode
                Case Settings.Mode.Carrier
                    ProgramMode.Text = "CARRIER"
                Case Settings.Mode.CarrierOnly
                    ProgramMode.Text = "CARRIER ONLY"
                Case Settings.Mode.Oper
                    ProgramMode.Text = "OPERATOR"
                Case Settings.Mode.OperOnly
                    ProgramMode.Text = "OPERATOR ONLY"
            End Select
        Catch e As Exception
            logWriter = New System.IO.StreamWriter(logWriterFileName)
            Logging("ERROR: Cant read settings: " + e.Message)
            logWriter.Close()
            Button1.Enabled = False
        End Try
    End Sub

    Private Sub ReadSearchCodes()
        utlCodeMap.Clear()
        aigaimCodeToAiCodeMap.Clear()
        Try
            Dim searchCodesjson As String = File.ReadAllText("dhq.ai2utl.search-codes.json")
            searchCodes = JsonConvert.DeserializeObject(Of SearchCode())(searchCodesjson)
            For Each searchCode As SearchCode In searchCodes
                Dim aiCodeArray() As String = searchCode.AiCode.Split(";"c)
                utlCodeMap.Add(searchCode.UtlCode, aiCodeArray)
                If (Len(searchCode.AigaimAiCode) > 0) Then
                    Dim aigaimCodeArray() As String = searchCode.AigaimAiCode.Split(";"c)
                    For Each aigaimCode As String In aigaimCodeArray
                        If Not aigaimCodeToAiCodeMap.ContainsKey(aigaimCode) Then
                            aigaimCodeToAiCodeMap.Add(aigaimCode, aiCodeArray.First)
                        End If
                    Next
                End If
            Next
        Catch e As System.IO.FileNotFoundException
            Logging("ERROR: Cant read settings file")
        End Try
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Cursor = Cursors.WaitCursor
        Application.DoEvents()
        Button1.Enabled = False
        logWriter = New System.IO.StreamWriter(logWriterFileName)
        totalCount = 0
        aircraftMap.Clear()
        aircraftNotUsedSet.Clear()
        OutputTextBox.Clear()

        Try
            If ReadRepaintsXml() = False Then
                Return
            End If

            ReadFlaiFolder()
            Logging("Flai Ai Models              :    " + CStr(totalAiModels))
            ReadAIGAIMFolder()
            Logging("Total With AIGAIM Ai Models :    " + CStr(totalAiModels))
            ReadMiscAiFolder()
            Logging("Total With Misc Ai Models   :    " + CStr(totalAiModels))

            Logging("")
            Logging("=============================================================")
            Dim totalUtlAircraft As Integer = 0
            For Each repaintfleet As repaints_informationRepaint_fleet In repaintsInformation.repaints()
                ProgressLabel.Text = "Processing: Utl Aircraft: " + repaintfleet.equip + " | Carrier: " + repaintfleet.car + " | Operator: " + repaintfleet.oper
                ProgressBar.Value = 100 * totalUtlAircraft / repaintsInformation.repaints().Count

                Application.DoEvents()
                If (settings.programMode = Settings.Mode.Carrier Or settings.programMode = Settings.Mode.CarrierOnly) Then
                    If BuildRepaintVisList(repaintfleet, repaintfleet.car, "CARRIER") = 0 And settings.programMode = Settings.Mode.Carrier Then
                        BuildRepaintVisList(repaintfleet, repaintfleet.oper, "OPERATOR")
                    End If
                Else
                    If BuildRepaintVisList(repaintfleet, repaintfleet.oper, "OPERATOR") = 0 And settings.programMode = Settings.Mode.Oper Then
                        BuildRepaintVisList(repaintfleet, repaintfleet.car, "CARRIER")
                    End If
                End If
                totalUtlAircraft = totalUtlAircraft + 1
            Next

            Logging("")
            Logging("=============================================================")
            Logging("")
            Logging("Total UTL Model processed:" + CStr(totalUtlAircraft))
            Logging("Total Ai Replaced:        " + CStr(totalCount))
            Logging("")
            Logging("=============================================================")
            OutputTextBox.ScrollToCaret()
            Dim output As New System.IO.StreamWriter(replacementFile)
            repaintsSer.Serialize(output, repaintsInformation)
            output.Close()
            logWriter.Close()
            Dim unusedWriter As System.IO.StreamWriter = New System.IO.StreamWriter("dhq.ai2utl.unused.txt")
            For Each remaining As String In aircraftNotUsedSet
                unusedWriter.Write(remaining + Environment.NewLine)
            Next
            unusedWriter.Close()
        Finally
            Cursor = Cursors.Default
            Button1.Enabled = True
        End Try

    End Sub
    Private Function ReadRepaintsXml() As Boolean
        Dim xmldoc As New XmlDocument

        Try
            xmldoc.Load(repaintFile)
            Dim allText As String = xmldoc.InnerXml

            ' Re-Read the search code every time the button is pressed
            ReadSearchCodes()

            ' Load repaints.xml
            Using currentStringReader As New StringReader(allText)
                repaintsInformation = repaintsSer.Deserialize(currentStringReader)
            End Using
        Catch e As Exception
            Logging("Error loading repaints file:" + e.Message)
            Return False
        End Try
        Return True
    End Function

    Private Sub ReadFlaiFolder()
        Try
            If (settings.flai.path.Length > 0) Then
                Dim flaiDir As DirectoryInfo = New DirectoryInfo(settings.flai.path)
                For Each aircraftFolder As DirectoryInfo In flaiDir.GetDirectories()

                    If (aircraftFolder.Name.StartsWith("FLAi_")) Then
                        Dim aiCode As String = aircraftFolder.Name.Substring(5, aircraftFolder.Name.Length - 5)
                        Dim aircraftFileName = aircraftFolder.FullName + "\aircraft.cfg"
                        ReadAirCraftCfg(aircraftFileName, aircraftMap, aiCode)
                    End If
                Next
            End If
        Catch e As Exception
            Logging("Error reading FLAI Path:" + e.Message)
        End Try
    End Sub

    Private Sub ReadAIGAIMFolder()
        Try
            If (settings.aigaim.path.Length > 0) Then
                Dim aigaimDir As DirectoryInfo = New DirectoryInfo(settings.aigaim.path)
                For Each aircraftFolder As DirectoryInfo In aigaimDir.GetDirectories()
                    If (aircraftFolder.Name.StartsWith("AIGAIM_")) Then
                        Dim aigaimDirPart As String = aircraftFolder.Name.Substring(7, aircraftFolder.Name.Length - 7)
                        For Each aigAimPattern As String In aigaimCodeToAiCodeMap.Keys
                            If aigaimDirPart.Contains(aigAimPattern) Then
                                Dim aircraftFileName = aircraftFolder.FullName + "\aircraft.cfg"
                                Dim aiCode As String = aigaimCodeToAiCodeMap(aigAimPattern)
                                ReadAirCraftCfg(aircraftFileName, aircraftMap, aiCode)
                            End If
                        Next
                    End If
                Next
            End If
        Catch e As Exception
            Logging("Error reading AIGAIM Path:" + e.Message)
        End Try
    End Sub


    Private Sub ReadMiscAiFolder()
        Try
            For Each miscai As Miscai In settings.miscai
                If (miscai.path.Length > 0) Then
                    For Each ai2UtlFile As String In My.Computer.FileSystem.GetFiles(
                        miscai.path,
                        Microsoft.VisualBasic.FileIO.SearchOption.SearchAllSubDirectories, "*.ai2utl")
                        Dim fileInfo As System.IO.FileInfo
                        fileInfo = My.Computer.FileSystem.GetFileInfo(ai2UtlFile)
                        Dim folderPath As String = fileInfo.DirectoryName
                        Dim aiCode As String = fileInfo.Name.Substring(0, fileInfo.Name.Length - 7)
                        Dim aircraftFileName = folderPath + "\aircraft.cfg"
                        ReadAirCraftCfg(aircraftFileName, aircraftMap, aiCode)
                    Next
                End If
            Next
        Catch e As Exception
            Logging("Error reading MiscAI Path:" + e.Message)
        End Try
    End Sub

    Private Sub ReadAirCraftCfg(filename As String, aircraftMap As Dictionary(Of String, HashSet(Of String)), aiCode As String)
        Dim reader = File.OpenText(filename)

        Dim line As String = Nothing
        Dim title As String = Nothing
        Dim parking_codes As String
        Dim lines As Integer = 0

        While (reader.Peek() <> -1)
            line = reader.ReadLine()
                If line.StartsWith("title=") Then
                    title = line.Substring(6, line.Length - 6)
                End If
                If line.StartsWith("atc_parking_codes=") Then
                    parking_codes = line.Substring(18, line.Length - 18)
                    Dim parkingCodeArray() As String = parking_codes.Split(","c)
                    For Each parkingCode As String In parkingCodeArray
                        Dim key As String = MakeKey(aiCode, parkingCode)
                        If (aircraftMap.ContainsKey(key)) Then
                            aircraftMap.Item(key).Add(title)
                        Else
                            Dim aircraftList As New HashSet(Of String)
                            aircraftList.Add(title)
                            aircraftMap.Add(key, aircraftList)
                        End If
                        aircraftNotUsedSet.Add(title)
                        totalAiModels = totalAiModels + 1
                    Next
                End If
        End While

    End Sub

    Private Function BuildRepaintVisList(repaintfleet As repaints_informationRepaint_fleet, airLine As String, type As String) As Integer
        Dim modCount = 0
        Dim newRepaintVisList As New List(Of repaints_informationRepaint_fleetRepaint_visual)
        Dim count As Integer=0
        Try
            If (settings.addOnlyMissing = True) Then
                If (repaintfleet.vis.Count > 1) Then
                    Return 0
                ElseIf repaintfleet.vis.Count = 1 Then
                    If (Not repaintfleet.vis.ElementAt(0).title.Contains("Daedalus")) Then
                        Return 0
                    End If
                End If
            End If

            If (utlCodeMap.ContainsKey(repaintfleet.equip)) Then
                For Each aiCode As String In utlCodeMap(repaintfleet.equip)
                    If (airLine.Length > 3) Then
                        airLine = airLine.Substring(0, 3)
                    End If
                    Dim key As String = MakeKey(aiCode, airLine)
                    If aircraftMap.ContainsKey(key) Then
                        Dim airCraftList As HashSet(Of String) = aircraftMap.Item(key)
                        For Each airCraftname As String In airCraftList
                            Dim newRepaintVis As New repaints_informationRepaint_fleetRepaint_visual
                            newRepaintVis.title = airCraftname
                            newRepaintVis.useOnce = False
                            newRepaintVisList.Add(newRepaintVis)
                            count = count + 1
                            If (count = settings.maxRepaints) Then
                                Exit Try
                            End If
                        Next
                    End If
                Next
            End If
        Finally
        End Try

        If newRepaintVisList.Count > 0 Then
            Dim newRepaintVisArray(newRepaintVisList.Count) As repaints_informationRepaint_fleetRepaint_visual
            Dim ratio As Integer = Math.Floor(100 / count)
            Dim firstRatio As Integer =  (100-ratio*count)
            Logging("Replace Utl Aircraft:" + repaintfleet.equip + "| Carrier: " + repaintfleet.car + " | Operator: " + repaintfleet.oper + " | Found:" + type)

            For Each newRepaintVis As repaints_informationRepaint_fleetRepaint_visual In newRepaintVisList
                aircraftNotUsedSet.Remove(newRepaintVis.title)
                newRepaintVis.val = ratio
                If firstRatio > 0 Then
                    newRepaintVis.val = newRepaintVis.val + 1
                    firstRatio = firstRatio - 1
                End If

            Next
            For Each repaintVis In newRepaintVisList
                newRepaintVisArray(modCount) = repaintVis
                modCount = modCount + 1
                Logging("  "+repaintVis.title)
            Next
            repaintfleet.vis = newRepaintVisArray
            totalCount = totalCount + modCount
        End If
        Return newRepaintVisList.Count
    End Function

    Private Sub Logging(entry As String)
        logWriter.Write(entry+ Environment.NewLine)
        OutputTextBox.AppendText(entry+ Environment.NewLine)
    End Sub

    Private Function MakeKey(aiCode As String, airline As String) As String
        If (airline.Length > 3) Then
            ' Disregard AALX, just AAL
            Return aiCode + "-" + airline.Substring(0, 3)
        End If
        Return aiCode + "-" + airline

    End Function

    Private Sub Label2_Click(sender As Object, e As EventArgs)

    End Sub
End Class




Public Class SearchCode
    Public UtlCode As String
    Public AigaimAiCode As String
    Public AiCode As String
End Class

Public Class SearchCodes
    Public SearchCode(100) As SearchCode
End Class
Public Class Utl
    Public repaintFileName As String
    Public path As String
End Class

Public Class Flai
    Public path As String
End Class

Public Class Aigaim
    Public path As String
End Class

Public Class Miscai
    Public path As String
End Class

Public Class Settings
    Enum Mode
        Carrier
        CarrierOnly
        Oper
        OperOnly
    End Enum
    Public utl As Utl
    Public flai As Flai
    Public aigaim As Aigaim
    Public miscai() As Miscai
    Public programMode As Mode
    Public addOnlyMissing As Boolean
    Public maxRepaints As Integer
End Class



' NOTE: Generated code may require at least .NET Framework 4.5 or .NET Core/Standard 2.0.
'''<remarks/>
<System.SerializableAttribute(),
 System.ComponentModel.DesignerCategoryAttribute("code"),
 System.Xml.Serialization.XmlTypeAttribute(AnonymousType:=True),
 System.Xml.Serialization.XmlRootAttribute([Namespace]:="", IsNullable:=False)>
Partial Public Class repaints_information

    Private repaintsField() As repaints_informationRepaint_fleet

    '''<remarks/>
    <System.Xml.Serialization.XmlArrayItemAttribute("repaint_fleet", IsNullable:=False)>
    Public Property repaints() As repaints_informationRepaint_fleet()
        Get
            Return Me.repaintsField
        End Get
        Set
            Me.repaintsField = Value
        End Set
    End Property
End Class

'''<remarks/>
<System.SerializableAttribute(),
 System.ComponentModel.DesignerCategoryAttribute("code"),
 System.Xml.Serialization.XmlTypeAttribute(AnonymousType:=True)>
Partial Public Class repaints_informationRepaint_fleet

    Private equipField As String

    Private carField As String

    Private operField As String

    Private visField() As repaints_informationRepaint_fleetRepaint_visual

    '''<remarks/>
    Public Property equip() As String
        Get
            Return Me.equipField
        End Get
        Set
            Me.equipField = Value
        End Set
    End Property

    '''<remarks/>
    Public Property car() As String
        Get
            Return Me.carField
        End Get
        Set
            Me.carField = Value
        End Set
    End Property

    '''<remarks/>
    Public Property oper() As String
        Get
            Return Me.operField
        End Get
        Set
            Me.operField = Value
        End Set
    End Property

    '''<remarks/>
    <System.Xml.Serialization.XmlArrayItemAttribute("repaint_visual", IsNullable:=False)>
    Public Property vis() As repaints_informationRepaint_fleetRepaint_visual()
        Get
            Return Me.visField
        End Get
        Set
            Me.visField = Value
        End Set
    End Property
End Class

'''<remarks/>
<System.SerializableAttribute(),
 System.ComponentModel.DesignerCategoryAttribute("code"),
 System.Xml.Serialization.XmlTypeAttribute(AnonymousType:=True)>
Partial Public Class repaints_informationRepaint_fleetRepaint_visual

    Private titleField As String

    Private valField As Byte

    Private useOnceField As Boolean

    '''<remarks/>
    Public Property title() As String
        Get
            Return Me.titleField
        End Get
        Set
            Me.titleField = Value
        End Set
    End Property

    '''<remarks/>
    Public Property val() As Byte
        Get
            Return Me.valField
        End Get
        Set
            Me.valField = Value
        End Set
    End Property

    '''<remarks/>
    Public Property useOnce() As Boolean
        Get
            Return Me.useOnceField
        End Get
        Set
            Me.useOnceField = Value
        End Set
    End Property
End Class
