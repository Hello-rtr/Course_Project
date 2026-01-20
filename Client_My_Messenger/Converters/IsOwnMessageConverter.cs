using Client_My_Messenger.Models;
using Client_My_Messenger.Pages;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Client_My_Messenger.Converters
{
    public class IsOwnMessageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MessageUserInChat message)
            {
                try
                {
                    var currentUser = AutorizationPage.currentUser;
                    return message?.User?.Id == currentUser?.Id;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
