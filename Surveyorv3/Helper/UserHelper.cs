using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace Surveyor.Helper
{
    public static class UserHelper
    {
        public static async Task<string> GetUserFullNameAsync()
        {
            try
            {
                // Get all users on the system
                var users = await User.FindAllAsync();

                if (users.Any())
                {
                    var currentUser = users.FirstOrDefault();
                    if (currentUser != null)
                    {
                        // Try to get the user's full name
                        var fullName = await currentUser.GetPropertyAsync(KnownUserProperties.DisplayName) as string;
                        return fullName ?? "Unknown User";
                    }
                }

                return "No users found";
            }
            catch (Exception ex)
            {
                // Handle errors (e.g., permissions issue)
                return $"Error: {ex.Message}";
            }
        }
    }

}
