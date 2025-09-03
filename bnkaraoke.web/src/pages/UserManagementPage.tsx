// src/pages/UserManagementPage.tsx
import React, { useState, useEffect, useCallback } from "react";
import { useNavigate } from "react-router-dom";
import { API_ROUTES } from "../config/apiConfig";
import "./UserManagementPage.css";
import { User } from "../types";

interface ExtendedUser extends User {
  id: string;
  userName: string;
  roles: string[];
  password?: string;
  mustChangePassword: boolean;
}

const UserManagementPage: React.FC = () => {
  const navigate = useNavigate();
  const [users, setUsers] = useState<ExtendedUser[]>([]);
  const [roles, setRoles] = useState<string[]>([]);
  const [pinCode, setPinCode] = useState<string>("");
  const [pinError, setPinError] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [newUser, setNewUser] = useState({ userName: "", firstName: "", lastName: "", mustChangePassword: true });
  const [editUser, setEditUser] = useState<ExtendedUser | null>(null);
  const [showPinModal, setShowPinModal] = useState(false);

  const validateToken = useCallback(() => {
    const token = localStorage.getItem("token");
    const userName = localStorage.getItem("userName");
    if (!token || !userName) {
      console.error("[USER_MANAGEMENT] No token or userName found");
      setError("Authentication token or username missing. Please log in again.");
      navigate("/login");
      return null;
    }

    try {
      if (token.split('.').length !== 3) {
        console.error("[USER_MANAGEMENT] Malformed token: does not contain three parts");
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Invalid token format. Please log in again.");
        navigate("/login");
        return null;
      }

      const payload = JSON.parse(atob(token.split('.')[1]));
      const exp = payload.exp * 1000;
      if (exp < Date.now()) {
        console.error("[USER_MANAGEMENT] Token expired:", new Date(exp).toISOString());
        localStorage.removeItem("token");
        localStorage.removeItem("userName");
        setError("Session expired. Please log in again.");
        navigate("/login");
        return null;
      }
      console.log("[USER_MANAGEMENT] Token validated:", { userName, exp: new Date(exp).toISOString() });
      return token;
    } catch (err) {
      console.error("[USER_MANAGEMENT] Token validation error:", err);
      localStorage.removeItem("token");
      localStorage.removeItem("userName");
      setError("Invalid token. Please log in again.");
      navigate("/login");
      return null;
    }
  }, [navigate]);

  useEffect(() => {
    const token = validateToken();
    if (!token) return;

    const storedRoles = localStorage.getItem("roles");
    if (storedRoles) {
      const parsedRoles = JSON.parse(storedRoles);
      if (!parsedRoles.includes("User Manager")) {
        console.log("[USER_MANAGEMENT] User lacks User Manager role, redirecting to dashboard");
        setError("You do not have permission to access user management. User Manager role required.");
        navigate("/dashboard");
        return;
      }
    } else {
      console.log("[USER_MANAGEMENT] No roles found, redirecting to login");
      setError("No roles found. Please log in again.");
      navigate("/login");
      return;
    }

    fetchUsers(token);
    fetchRoles(token);
    fetchPinCode(token);
  }, [navigate, validateToken]);

  const fetchUsers = async (token: string) => {
    try {
      console.log(`[USER_MANAGEMENT] Fetching users from: ${API_ROUTES.USERS}`);
      const response = await fetch(API_ROUTES.USERS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] Users Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to fetch users: ${response.status} ${response.statusText} - ${responseText}`);
      }
      const data: ExtendedUser[] = JSON.parse(responseText);
      setUsers(data);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to fetch users due to server authorization error. Please contact support." : err.message : "Failed to fetch users. Please try again.";
      setError(errorMessage);
      setUsers([]);
      console.error("[USER_MANAGEMENT] Fetch Users Error:", err);
    }
  };

  const fetchRoles = async (token: string) => {
    try {
      console.log(`[USER_MANAGEMENT] Fetching roles from: ${API_ROUTES.USER_ROLES}`);
      const response = await fetch(API_ROUTES.USER_ROLES, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] Roles Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to fetch roles: ${response.status} ${response.statusText} - ${responseText}`);
      }
      const data: string[] = JSON.parse(responseText);
      setRoles(data);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to fetch roles due to server authorization error. Please contact support." : err.message : "Failed to fetch roles. Please try again.";
      setError(errorMessage);
      setRoles([]);
      console.error("[USER_MANAGEMENT] Fetch Roles Error:", err);
    }
  };

  const fetchPinCode = async (token: string) => {
    try {
      console.log(`[USER_MANAGEMENT] Fetching PIN code from: ${API_ROUTES.REGISTRATION_SETTINGS}`);
      const response = await fetch(API_ROUTES.REGISTRATION_SETTINGS, {
        headers: { Authorization: `Bearer ${token}` },
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] PIN Code Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to fetch PIN code: ${response.status} ${response.statusText} - ${responseText}`);
      }
      const data = JSON.parse(responseText);
      setPinCode(data.pinCode || "");
      setPinError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to fetch PIN code due to server authorization error. Please contact support." : err.message : "Failed to fetch PIN code. Please try again.";
      setPinError(errorMessage);
      setPinCode("");
      console.error("[USER_MANAGEMENT] Fetch PIN Code Error:", err);
    }
  };

  const handleAddUser = async () => {
    const token = validateToken();
    if (!token) return;

    if (!newUser.userName) {
      setError("Please enter a phone number for the new user");
      return;
    }
    try {
      const payload = {
        phoneNumber: newUser.userName,
        firstName: newUser.firstName,
        lastName: newUser.lastName,
        mustChangePassword: newUser.mustChangePassword
      };
      console.log("[USER_MANAGEMENT] Add User Payload:", JSON.stringify(payload));
      const response = await fetch(API_ROUTES.ADD_USER, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify(payload),
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] Add User Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to add user: ${response.status} ${response.statusText} - ${responseText}`);
      }
      alert("User added successfully! Temporary password: Pwd1234.");
      setNewUser({ userName: "", firstName: "", lastName: "", mustChangePassword: true });
      fetchUsers(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to add user due to server authorization error. Please contact support." : err.message : "Failed to add user. Please try again.";
      setError(errorMessage);
      console.error("[USER_MANAGEMENT] Add User Error:", err);
    }
  };

  const handleUpdateUser = async () => {
    if (!editUser) return;
    const token = validateToken();
    if (!token) return;

    try {
      console.log(`[USER_MANAGEMENT] Updating user: ${editUser.userName}`);
      const response = await fetch(API_ROUTES.UPDATE_USER, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({
          userId: editUser.id,
          userName: editUser.userName,
          password: editUser.password || null,
          firstName: editUser.firstName,
          lastName: editUser.lastName,
          roles: editUser.roles
        }),
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] Update User Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to update user: ${response.status} ${response.statusText} - ${responseText}`);
      }
      alert("User updated successfully!");
      setEditUser(null);
      fetchUsers(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to update user due to server authorization error. Please contact support." : err.message : "Failed to update user. Please try again.";
      setError(errorMessage);
      console.error("[USER_MANAGEMENT] Update User Error:", err);
    }
  };

  const handleDeleteUser = async (userId: string) => {
    const token = validateToken();
    if (!token) return;

    if (!window.confirm("Are you sure you want to delete this user?")) return;
    try {
      console.log(`[USER_MANAGEMENT] Deleting user ${userId}`);
      const response = await fetch(API_ROUTES.DELETE_USER, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ userId }),
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] Delete User Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to delete user: ${response.status} ${response.statusText} - ${responseText}`);
      }
      alert("User deleted successfully!");
      setEditUser(null);
      fetchUsers(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to delete user due to server authorization error. Please contact support." : err.message : "Failed to delete user. Please try again.";
      setError(errorMessage);
      console.error("[USER_MANAGEMENT] Delete User Error:", err);
    }
  };

  const handleForcePasswordChange = async (userId: string, mustChangePassword: boolean) => {
    const token = validateToken();
    if (!token) return;

    try {
      console.log(`[USER_MANAGEMENT] Setting MustChangePassword to ${mustChangePassword} for user ${userId}`);
      const response = await fetch(`${API_ROUTES.FORCE_PASSWORD_CHANGE}/${userId}/force-password-change`, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ mustChangePassword }),
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] Force Password Change Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to update password change requirement: ${response.status} ${response.statusText} - ${responseText}`);
      }
      alert(`Password change requirement ${mustChangePassword ? "enabled" : "disabled"} successfully!`);
      fetchUsers(token);
      setError(null);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to update password change requirement due to server authorization error. Please contact support." : err.message : "Failed to update password change requirement. Please try again.";
      setError(errorMessage);
      console.error("[USER_MANAGEMENT] Force Password Change Error:", err);
    }
  };

  const handleUpdatePinCode = async () => {
    const token = validateToken();
    if (!token) return;

    if (!pinCode || pinCode.length !== 6 || !/^\d+$/.test(pinCode)) {
      setPinError("PIN code must be exactly 6 digits");
      return;
    }
    try {
      console.log(`[USER_MANAGEMENT] Updating PIN code to: ${pinCode}`);
      const response = await fetch(API_ROUTES.REGISTRATION_SETTINGS, {
        method: "PATCH",
        headers: {
          "Content-Type": "application/json",
          Authorization: `Bearer ${token}`,
        },
        body: JSON.stringify({ pinCode }),
      });
      const responseText = await response.text();
      console.log("[USER_MANAGEMENT] Update PIN Code Raw Response:", responseText);
      if (!response.ok) {
        throw new Error(`Failed to update PIN code: ${response.status} ${response.statusText} - ${responseText}`);
      }
      alert("PIN code updated successfully!");
      setShowPinModal(false);
      setPinError(null);
      fetchPinCode(token);
    } catch (err) {
      const errorMessage = err instanceof Error ? err.message.includes("AuthorizationPolicy") ? "Unable to update PIN code due to server authorization error. Please contact support." : err.message : "Failed to update PIN code. Please try again.";
      setPinError(errorMessage);
      console.error("[USER_MANAGEMENT] Update PIN Code Error:", err);
    }
  };

  const openEditUser = (user: ExtendedUser) => {
    setEditUser({ ...user, password: "" });
  };

  try {
    return (
      <div className="user-management-container mobile-user-management">
        <header className="user-management-header">
          <h1 className="user-management-title">User Management</h1>
          <div className="header-buttons">
            <button 
              className="action-button pin-button" 
              onClick={() => setShowPinModal(true)}
              onTouchStart={() => setShowPinModal(true)}
            >
              Manage Registration PIN
            </button>
            <button 
              className="action-button back-button" 
              onClick={() => navigate("/dashboard")}
              onTouchStart={() => navigate("/dashboard")}
            >
              Back to Dashboard
            </button>
          </div>
        </header>
        <div className="card-container">
          <section className="user-management-card edit-users-card">
            <h2 className="section-title">Edit Users</h2>
            {error && <p className="error-text">{error}</p>}
            {users.length > 0 ? (
              <ul className="user-list">
                {users.map((user) => (
                  <li 
                    key={user.id} 
                    className="user-item" 
                    onClick={() => openEditUser(user)}
                    onTouchStart={() => openEditUser(user)}
                  >
                    <span className="user-name">{`${user.firstName} ${user.lastName}`}</span>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="user-management-text">{error ? "Failed to load users. Please try again or contact support." : "No users found."}</p>
            )}
          </section>
          <section className="user-management-card add-user-card">
            <h2 className="section-title">Add New User</h2>
            <div className="add-user-form">
              <label className="form-label">Phone Number</label>
              <input
                type="text"
                value={newUser.userName}
                onChange={(e) => setNewUser({ ...newUser, userName: e.target.value })}
                placeholder="Phone Number (e.g., 1234567890)"
                className="form-input"
              />
              <label className="form-label">First Name</label>
              <input
                type="text"
                value={newUser.firstName}
                onChange={(e) => setNewUser({ ...newUser, firstName: e.target.value })}
                placeholder="First Name"
                className="form-input"
              />
              <label className="form-label">Last Name</label>
              <input
                type="text"
                value={newUser.lastName}
                onChange={(e) => setNewUser({ ...newUser, lastName: e.target.value })}
                placeholder="Last Name"
                className="form-input"
              />
              <label className="form-checkbox">
                <input
                  type="checkbox"
                  checked={newUser.mustChangePassword}
                  onChange={(e) => setNewUser({ ...newUser, mustChangePassword: e.target.checked })}
                />
                Force Password Reset
              </label>
              <button 
                className="action-button add-button" 
                onClick={handleAddUser}
                onTouchStart={handleAddUser}
              >
                Add User
              </button>
            </div>
          </section>
        </div>

        {editUser && (
          <div className="modal-overlay mobile-user-management">
            <div className="modal-content edit-user-modal">
              <h2 className="modal-title">Edit User</h2>
              <div className="add-user-form">
                <label className="form-label">Phone Number</label>
                <input
                  type="text"
                  value={editUser.userName}
                  onChange={(e) => setEditUser({ ...editUser, userName: e.target.value })}
                  placeholder="Phone Number"
                  className="form-input"
                />
                <label className="form-label">Password (leave blank to keep current)</label>
                <input
                  type="password"
                  value={editUser.password || ""}
                  onChange={(e) => setEditUser({ ...editUser, password: e.target.value })}
                  placeholder="New Password (optional)"
                  className="form-input"
                />
                <label className="form-label">First Name</label>
                <input
                  type="text"
                  value={editUser.firstName}
                  onChange={(e) => setEditUser({ ...editUser, firstName: e.target.value })}
                  placeholder="First Name"
                  className="form-input"
                />
                <label className="form-label">Last Name</label>
                <input
                  type="text"
                  value={editUser.lastName}
                  onChange={(e) => setEditUser({ ...editUser, lastName: e.target.value })}
                  placeholder="Last Name"
                  className="form-input"
                />
                <label className="form-label">Roles</label>
                <div className="role-checkboxes">
                  {roles.length > 0 ? (
                    roles.map((role) => (
                      <label key={role} className="role-checkbox">
                        <input
                          type="checkbox"
                          checked={editUser.roles.includes(role)}
                          onChange={(e) => {
                            const updatedRoles = e.target.checked
                              ? [...editUser.roles, role]
                              : editUser.roles.filter((r: string) => r !== role);
                            setEditUser({ ...editUser, roles: updatedRoles });
                          }}
                        />
                        <span>{role}</span>
                      </label>
                    ))
                  ) : (
                    <p className="error-text">No roles available. Please try again or contact support.</p>
                  )}
                </div>
                <div className="modal-buttons">
                  <button
                    className={`action-button ${editUser.mustChangePassword ? "disable-button" : "enable-button"}`}
                    onClick={() => handleForcePasswordChange(editUser.id, !editUser.mustChangePassword)}
                    onTouchStart={() => handleForcePasswordChange(editUser.id, !editUser.mustChangePassword)}
                  >
                    {editUser.mustChangePassword ? "Don’t Force Password Change" : "Force Password Change"}
                  </button>
                  <button 
                    className="action-button update-button" 
                    onClick={handleUpdateUser}
                    onTouchStart={handleUpdateUser}
                  >
                    Update
                  </button>
                  <button
                    className="action-button delete-button"
                    onClick={() => handleDeleteUser(editUser.id)}
                    onTouchStart={() => handleDeleteUser(editUser.id)}
                  >
                    Delete
                  </button>
                  <button
                    className="action-button cancel-button"
                    onClick={() => setEditUser(null)}
                    onTouchStart={() => setEditUser(null)}
                  >
                    Cancel
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}

        {showPinModal && (
          <div className="modal-overlay mobile-user-management">
            <div className="modal-content pin-modal">
              <h2 className="modal-title">Manage Registration PIN</h2>
              {pinError && <p className="error-text">{pinError}</p>}
              <div className="pin-code-form">
                <label className="form-label">PIN Code</label>
                <input
                  type="text"
                  value={pinCode}
                  onChange={(e) => setPinCode(e.target.value)}
                  placeholder="Enter 6-digit PIN"
                  className="form-input"
                  maxLength={6}
                />
                <div className="modal-buttons">
                  <button 
                    className="action-button save-button" 
                    onClick={handleUpdatePinCode}
                    onTouchStart={handleUpdatePinCode}
                  >
                    Save PIN
                  </button>
                  <button
                    className="action-button cancel-button"
                    onClick={() => setShowPinModal(false)}
                    onTouchStart={() => setShowPinModal(false)}
                  >
                    Cancel
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    );
  } catch (error) {
    console.error("[USER_MANAGEMENT] Render error:", error);
    return <div>Error in UserManagementPage: {error instanceof Error ? error.message : 'Unknown error'}</div>;
  }
};

export default UserManagementPage;