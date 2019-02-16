
Imports System.IO
Imports System.Text.RegularExpressions
Imports System.Xml
Imports System.Xml.Serialization
Imports Newtonsoft.Json

Public Class Form1
    Dim aircraftMap As New Dictionary(Of String, HashSet(Of String))
    Dim totalCount As Integer = 0
    Dim utlCodeMap As New Dictionary(Of String, String())
    Dim settings As Settings
    Dim searchCodes() As SearchCode
    Dim logWriter As New System.IO.StreamWriter("dhq.ai2utl.log.txt")
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
        Catch e As System.IO.FileNotFoundException
                Logging("ERROR: Cant read settings file")
        End Try
        replacementFile = settings.utl.path +"\dhq.ai2utl.replacement.result.xml"
        ReplacementFileLabel.Text = replacementFile
        
        repaintFile =  settings.utl.path +"\"+ settings.utl.repaintFileName
        RepaintFileLabel.Text = repaintFile
        If settings.addOnlyMissing = False Then
            ReplaceAllLabel.Text = "False"
        Else
            ReplaceAllLabel.Text = "True"
        End If
        If settings.includeOper = False Then
            IncludeOper.Text = "False"
        Else
            IncludeOper.Text = "True"
        End If

    End Sub

    Private Sub ReadSearchCodes()
        utlCodeMap.Clear()

        Try
            Dim searchCodesjson As String = File.ReadAllText("dhq.ai2utl.search-codes.json")
            searchCodes = JsonConvert.DeserializeObject(Of SearchCode())(searchCodesjson)
            For Each searchCode As SearchCode In searchCodes
                Dim aiCodeArray() As String = searchCode.AiCode.Split(";"c)
                utlCodeMap.Add(searchCode.UtlCode, aiCodeArray)
            Next
        Catch e As System.IO.FileNotFoundException
            Logging("ERROR: Cant read settings file")
        End Try
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Cursor = Cursors.WaitCursor 
        Application.DoEvents()
        Try
            If ReadRepaintsXml() = False Then
                Return
            End If

            ReadFlaiFolder()
        Logging("Flai Ai Models :    " + CStr(totalAiModels))
        ReadMiscAiFolder()
        Logging("Total Ai Models:    " + CStr(totalAiModels))

        Logging("")
        Logging("=============================================================")
        Dim totalUtlAircraft As Integer = 0
        For Each repaintfleet As repaints_informationRepaint_fleet In repaintsInformation.repaints()
                If BuildRepaintVisList(repaintfleet, repaintfleet.car, "CAR") = 0 And settings.includeOper = True Then
                    BuildRepaintVisList(repaintfleet, repaintfleet.oper, "OPER")
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
        Finally
            Cursor = Cursors.Default
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
            Logging("Error reading FLAI Path:"+e.Message)
         End Try
    End Sub

    Private Sub ReadMiscAiFolder()
        Try
            For Each miscai As Miscai In settings.miscai
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
            Next
        Catch e As Exception
            Logging("Error reading FLAI Path:"+e.Message)
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
                    Dim key As String = MakeKey(aiCode,parkingCode)
                    If (aircraftMap.ContainsKey(key)) Then
                        aircraftMap.Item(aiCode + "-" + parkingCode).Add(title)
                    Else
                        Dim aircraftList As New HashSet(Of String)
                        aircraftList.Add(title)
                        aircraftMap.Add(key, aircraftList)
                    End If
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
            Logging("Replace Utl Aircraft:" + repaintfleet.equip + "| Carrier: " + repaintfleet.car + " | Operator: " + repaintfleet.oper +" | Mode:" +type )

            For Each newRepaintVis As repaints_informationRepaint_fleetRepaint_visual In newRepaintVisList
                newRepaintVis.val = ratio + firstRatio
                firstRatio=0
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
        Return aiCode+"-"+airline
    End Function

End Class




Public Class SearchCode
    Public UtlCode As String
    Public AiCode As String
    Public SearchIn As String
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

Public Class Miscai
    Public path As String
End Class
Public Class Settings
    Public utl As Utl
    Public flai As Flai
    Public miscai() As Miscai
    Public includeOper As Boolean
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
