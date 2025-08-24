export const getUserRole = () => {
    const user = JSON.parse(localStorage.getItem('user'));
    return user?.role || 'guest'; // Default to guest if no role is found
};

// Clear all authentication-related data from localStorage
export const clearAuthData = () => {
    localStorage.removeItem('token');
    localStorage.removeItem('userName');
    localStorage.removeItem('firstName');
    localStorage.removeItem('lastName');
    localStorage.removeItem('roles');
    localStorage.removeItem('mustChangePassword');
};

// Convenience helper to wipe auth data and navigate to login
export const logoutAndRedirect = (navigate) => {
    clearAuthData();
    navigate('/login', { replace: true });
};
