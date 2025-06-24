export const getUserRole = () => {
    const user = JSON.parse(localStorage.getItem('user'));
    return user?.role || 'guest'; // Default to guest if no role is found
};
