﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using OsEngine.Entity;

namespace OsEngine.OsMiner.Patterns
{
    /// <summary>
    /// Логика взаимодействия для AutoTestResultsUi.xaml
    /// </summary>
    public partial class AutoTestResultsUi
    {
        private List<TestResult> _testResults;

        public AutoTestResultsUi(List<TestResult> testResults)
        {
            InitializeComponent();
            _testResults = testResults;
            _grid = new DataGridView();
            CreateGridPatternsGrid(_grid, Host);
            PaintTable();
        }

        public event Action<TestResult> UserClickOnNewPattern; 

        private DataGridView _grid;

        void CreateGridPatternsGrid(DataGridView grid, WindowsFormsHost host)
        {

            grid.AllowUserToOrderColumns = true;
            grid.AllowUserToResizeRows = true;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToAddRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;

            DataGridViewCellStyle style = new DataGridViewCellStyle();
            style.Alignment = DataGridViewContentAlignment.TopLeft;
            style.WrapMode = DataGridViewTriState.True;
            grid.DefaultCellStyle = style;

            DataGridViewTextBoxCell cell0 = new DataGridViewTextBoxCell();
            cell0.Style = style;

            DataGridViewComboBoxCell cellComboBox = new DataGridViewComboBoxCell();
            cellComboBox.Style = style;

            DataGridViewColumn column0 = new DataGridViewColumn();
            column0.CellTemplate = cell0;
            column0.HeaderText = @"№";
            column0.ReadOnly = true;
            column0.Width = 40;

            grid.Columns.Add(column0);

            DataGridViewColumn column1 = new DataGridViewColumn();
            column1.CellTemplate = cell0;
            column1.HeaderText = @"Кол-во входов";
            column1.ReadOnly = true;
            column1.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add(column1);

            DataGridViewColumn column2 = new DataGridViewColumn();
            column2.CellTemplate = cell0;
            column2.HeaderText = @"Прибыльность";
            column2.ReadOnly = true;
            column2.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add(column2);

            DataGridViewColumn column4 = new DataGridViewColumn();
            column4.CellTemplate = cell0;
            column4.HeaderText = @"МО";
            column4.ReadOnly = true;
            column4.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns.Add(column4);

            grid.Rows.Add(null, null);

            host.Child = grid;
            grid.Click += grid_Click;
        }

        void grid_Click(object sender, EventArgs e)
        {
            if (_grid.SelectedCells.Count == 0)
            {
                return;
            }

            if (UserClickOnNewPattern != null)
            {
                UserClickOnNewPattern(_testResults[_grid.SelectedCells[0].RowIndex]);
            }
        }

        void PaintTable()
        {
            _grid.Rows.Clear();

            for (int i = 0; i < _testResults.Count; i++)
            {
                DataGridViewRow nRow = new DataGridViewRow();
                nRow.Cells.Add(new DataGridViewTextBoxCell());
                nRow.Cells[0].Value = i;
                nRow.Cells.Add(new DataGridViewTextBoxCell());
                nRow.Cells[1].Value = _testResults[i].Positions.Count;
                nRow.Cells.Add(new DataGridViewTextBoxCell());
                nRow.Cells[2].Value = GetProfit(_testResults[i].Positions);
                nRow.Cells.Add(new DataGridViewTextBoxCell());
                nRow.Cells[3].Value = GetMeadleProfit(_testResults[i].Positions);
                _grid.Rows.Add(nRow);
            }
        }

        private decimal GetProfit(List<Position> positions)
        {
            decimal result = 0;

            for (int i = 0; i < positions.Count; i++)
            {
                result += positions[i].ProfitPortfolioPunkt;
            }
            return result;
        }

        private decimal GetMeadleProfit(List<Position> positions)
        {
            decimal result = 0;

            for (int i = 0; i < positions.Count; i++)
            {
                result += positions[i].ProfitPortfolioPunkt;
            }

            if (positions.Count != 0)
            {
                result = result/positions.Count;
            }

            return result;
        }


    }
}