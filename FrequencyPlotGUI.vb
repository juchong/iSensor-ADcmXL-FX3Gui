﻿'File:          FrequencyPlotGUI.vb
'Author:        Alex Nolan (alex.nolan@analog.com)
'Date:          10/30/2019
'Description:   Plots FFT data for a DUT

Imports System.Windows.Forms.DataVisualization.Charting
Imports RegMapClasses
Imports SignalProcessing

Public Class FrequencyPlotGUI
    Inherits FormBase

    'FFT streamer
    Private WithEvents m_FFTStream As FFT_Streamer

    'selected register list
    Private selectedRegList As List(Of RegClass)

    'bool to track if currently plotting data
    Private running As Boolean

    Private Sub FrequencyPlotGUI_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        'instantiate fft streamer
        m_FFTStream = New FFT_Streamer(m_TopGUI.FX3, m_TopGUI.Dut)

        'populate register list dropdown
        For Each reg In m_TopGUI.RegMap
            regSelect.Items.Add(reg.Label)
        Next
        regSelect.SelectedIndex = 0

        'set up list view
        RegisterList.View = View.Details
        RegisterList.Columns.Add("Register", RegisterList.Width - 1, HorizontalAlignment.Left)

        'initialize variables
        selectedRegList = New List(Of RegClass)
        btn_stopPlot.Enabled = False
        running = False

        'set DR triggered register reads
        m_TopGUI.FX3.DrActive = True

    End Sub

    Private Sub Shutdown() Handles Me.Closing
        If running Then
            m_FFTStream.CancelAsync()
            running = False
            Threading.Thread.Sleep(250)
        End If
    End Sub

    Private Sub SetupPlot()
        'Reset the chart area
        dataPlot.ChartAreas.Clear()
        dataPlot.ChartAreas.Add(New ChartArea)

        'configure chart
        dataPlot.ChartAreas(0).AxisY.MajorGrid.Enabled = True
        dataPlot.ChartAreas(0).AxisX.MajorGrid.Enabled = True
        dataPlot.ChartAreas(0).AxisX.Title = "Frequency (Hz)"
        dataPlot.ChartAreas(0).AxisX.LabelStyle.Format = "#.##"
        dataPlot.ChartAreas(0).AxisY.Title = "FFT Magnitude"
        dataPlot.ChartAreas(0).AxisX.LogarithmBase = 10
        dataPlot.ChartAreas(0).AxisY.LogarithmBase = 10

        'Remove all existing series
        dataPlot.Series.Clear()

        'Add series for each register
        Dim temp As Series
        For Each reg In selectedRegList
            temp = New Series
            temp.ChartType = SeriesChartType.Line
            temp.BorderWidth = 2
            temp.Name = reg.Label
            dataPlot.Series.Add(temp)
        Next

    End Sub

    Private Sub SetupStream()
        'set length
        Dim length As UInteger
        Try
            length = Convert.ToUInt32(NFFT.Text)
        Catch ex As Exception
            MsgBox("ERROR: Invalid FFT length. Must be a 2^n value, for n = 1 to 14. Defaulting to 4096.")
            length = 4096
            NFFT.Text = "4096"
        End Try
        m_FFTStream.Length = length

        'set FFT averages
        Try
            m_FFTStream.NumAverages = Convert.ToUInt32(FFT_Averages.Text)
        Catch ex As Exception
            MsgBox("ERROR: Invalid number of FFT averages. Must be a positive integer. Defaulting to 1.")
            m_FFTStream.NumAverages = 1
            FFT_Averages.Text = "1"
        End Try

        selectedRegList.Clear()
        For Each item As ListViewItem In RegisterList.Items
            selectedRegList.Add(m_TopGUI.RegMap(item.Text))
        Next
        m_FFTStream.RegList = selectedRegList

        UpdateSampleFreq()
    End Sub

    Private Sub UpdateSampleFreq()
        Dim freq As Double = m_TopGUI.FX3.MeasurePinFreq(m_TopGUI.FX3.DrPin, 1, 5000, 2)
        m_FFTStream.SampleFrequency = freq
        drFreq.Text = FormatNumber(freq, 1).ToString() + "Hz"
    End Sub

    Private Sub UpdatePlot()
        'handle invokes after the form is closed
        If Me.IsDisposed Or Me.Disposing Then
            Exit Sub
        End If
        'add each new series
        For reg As Integer = 0 To selectedRegList.Count() - 1
            dataPlot.Series(reg).Points.Clear()
            For i As Integer = 0 To m_FFTStream.Result(reg).Count() - 1
                dataPlot.Series(reg).Points.AddXY(m_FFTStream.FrequencyRange(i), Math.Max(m_FFTStream.Result(reg)(i), 0.0000000001))
            Next
        Next
        'check if log axis needed
        dataPlot.ChartAreas(0).AxisX.IsLogarithmic = logXaxis.Checked
        dataPlot.ChartAreas(0).AxisY.IsLogarithmic = logYaxis.Checked
        'recalculate scale based on newest values
        dataPlot.ChartAreas(0).RecalculateAxesScale()
        UpdateSampleFreq()
    End Sub

    Private Sub FFTDone() Handles m_FFTStream.FFTDone
        'Whenever there is new FFT data update the plot
        Me.Invoke(New MethodInvoker(AddressOf UpdatePlot))
    End Sub

    Private Sub ResizeHandler() Handles Me.Resize
        dataPlot.Top = 9
        dataPlot.Left = 237
        dataPlot.Width = Me.Width - 259
        dataPlot.Height = Me.Height - 57
        dataPlot.ResetAutoValues()
    End Sub

    Private Sub btn_addreg_Click(sender As Object, e As EventArgs) Handles btn_addreg.Click
        Dim newItem As New ListViewItem()
        newItem.SubItems(0).Text = regSelect.SelectedItem
        RegisterList.Items.Add(newItem)
    End Sub

    Private Sub btn_removeReg_Click(sender As Object, e As EventArgs) Handles btn_removeReg.Click
        If IsNothing(Me.RegisterList.FocusedItem) Then
            MessageBox.Show("Please select an Item to Delete", "Remove register warning", MessageBoxButtons.OK)
        Else
            Me.RegisterList.Items.RemoveAt(Me.RegisterList.FocusedItem.Index)
        End If
    End Sub

    Private Sub btn_run_Click(sender As Object, e As EventArgs) Handles btn_run.Click
        'check there is a register selected
        If RegisterList.Items.Count = 0 Then
            MsgBox("ERROR: No registers selected")
            Exit Sub
        End If

        'set up stream (FFT_Streamer and register list)
        SetupStream()

        'set up plotting
        SetupPlot()

        'start async stream operation
        m_FFTStream.RunAync()

        'set running flag
        running = True

        'disable inputs
        NFFT.Enabled = False
        FFT_Averages.Enabled = False
        regSelect.Enabled = False
        btn_addreg.Enabled = False
        btn_removeReg.Enabled = False
        btn_stopPlot.Enabled = True
        btn_run.Enabled = False

    End Sub

    Private Sub btn_stopPlot_Click(sender As Object, e As EventArgs) Handles btn_stopPlot.Click
        'cancel running stream
        m_FFTStream.CancelAsync()

        'set running flag to false
        running = False

        'enable inputs
        NFFT.Enabled = True
        FFT_Averages.Enabled = True
        regSelect.Enabled = True
        btn_addreg.Enabled = True
        btn_removeReg.Enabled = True
        btn_run.Enabled = True
        btn_stopPlot.Enabled = False

    End Sub

End Class