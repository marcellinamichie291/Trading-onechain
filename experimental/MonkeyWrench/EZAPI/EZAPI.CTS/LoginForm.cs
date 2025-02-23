﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Resources;

namespace EZAPI
{
    public enum zLoginResult { OK, Cancel };
    public enum zLoginType { UserInteraction, Unattended };

    public partial class LoginForm : Form
    {
        public zLoginResult LoginResult { get { return _loginResult; } }
        public string Username { get { return txtUsername.Text.Trim(); } set { txtUsername.Text = value; } }
        public string Password { get { return txtPassword.Text.Trim(); } set { txtPassword.Text = value; } }
        public bool SaveLogin { get { return chkSaveLogin.Checked; } set { chkSaveLogin.Checked = value; } }

        private zLoginResult _loginResult;

        public LoginForm()
        {
            InitializeComponent();

            _loginResult = zLoginResult.Cancel;

            string username = "", password = "";
            IResourceReader reader = null;
            Dictionary<string, string> loginResources = new Dictionary<string, string>();
            try
            {
                reader = new ResourceReader("Login.resources");
                IDictionaryEnumerator dict = reader.GetEnumerator();
                while (dict.MoveNext())
                {
                    loginResources[dict.Key.ToString()] = dict.Value.ToString();
                }
                username = loginResources["TTUSERNAME"];
                password = loginResources["TTPASSWORD"];
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            finally
            {
                if (reader != null)
                    reader.Close();
            }

            SaveLogin = false;
            if (!username.Equals("") || !password.Equals(""))
                SaveLogin = true;

            Username = username;
            Password = password;
        }

        private void btnOK_Click(object sender, EventArgs e)
        {
            _loginResult = zLoginResult.OK;
            this.Close();
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            _loginResult = zLoginResult.Cancel;
            this.Close();
        }

        private void APILoginForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (SaveLogin == true)
            {
                IResourceWriter writer = new ResourceWriter("Login.resources");
                writer.AddResource("TTUSERNAME", Username);
                writer.AddResource("TTPASSWORD", Password);
                writer.Close();
            }

        }

        private void btnAccounts_Click(object sender, EventArgs e)
        {
            AccountForm acctForm = new AccountForm();
            acctForm.ShowDialog(this);
        }

        
    } // class
} // namespace
