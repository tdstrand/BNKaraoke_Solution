import React, { useState, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import './RegisterPage.css';
import LogoDuet from '../assets/TwoSingerMnt.png';
import { API_ROUTES } from '../config/apiConfig';

const RegisterPage: React.FC = () => {
  const [userName, setUserName] = useState<string>("");
  const [password, setPassword] = useState<string>("");
  const [confirmPassword, setConfirmPassword] = useState<string>("");
  const [firstName, setFirstName] = useState<string>("");
  const [lastName, setLastName] = useState<string>("");
  const [pinCode, setPinCode] = useState<string>("");
  const [error, setError] = useState<string>("");
  const navigate = useNavigate();
  const userNameRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const confirmPasswordRef = useRef<HTMLInputElement>(null);
  const firstNameRef = useRef<HTMLInputElement>(null);
  const lastNameRef = useRef<HTMLInputElement>(null);
  const pinCodeRef = useRef<HTMLInputElement>(null);

  // Format phone number as (xxx) xxx-xxxx
  const formatPhoneNumber = (value: string): string => {
    const digits = value.replace(/\D/g, "").slice(0, 10); // Keep only digits, max 10
    if (digits.length === 0) return "";
    if (digits.length <= 3) return `(${digits}`;
    if (digits.length <= 6) return `(${digits.slice(0, 3)}) ${digits.slice(3)}`;
    return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`;
  };

  // Handle phone input change
  const handlePhoneChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const rawValue = e.target.value.replace(/\D/g, ""); // Store raw digits
    setUserName(rawValue); // Store raw for submission
    e.target.value = formatPhoneNumber(rawValue); // Display formatted
  };

  const handleRegister = async () => {
    if (!userName || !password || !confirmPassword || !firstName || !lastName || !pinCode) {
      setError("Please fill in all fields");
      return;
    }
    if (password !== confirmPassword) {
      setError("Passwords do not match");
      return;
    }
    if (pinCode.length !== 6 || !/^\d+$/.test(pinCode)) {
      setError("PIN code must be exactly 6 digits");
      return;
    }
    const cleanPhone = userName.replace(/\D/g, "");
    try {
      console.log(`Attempting register fetch to: ${API_ROUTES.REGISTER}`);
      const response = await fetch(API_ROUTES.REGISTER, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          phoneNumber: cleanPhone,
          password: password,
          firstName: firstName,
          lastName: lastName,
          roles: ["Singer"],
          pinCode: pinCode
        }),
      });
      const responseText = await response.text();
      console.log("Register Raw Response:", responseText);
      if (!response.ok) {
        let errorData;
        try {
          errorData = JSON.parse(responseText);
        } catch {
          errorData = { message: "Registration failed" };
        }
        throw new Error(errorData.message || `Registration failed: ${response.status}`);
      }
      alert("Registration successful! Please log in.");
      navigate("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
      console.error("Register Error:", err);
    }
  };

  const handleKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "Enter") {
      if (!userName && userNameRef.current) {
        userNameRef.current.focus();
      } else if (!password && passwordRef.current) {
        passwordRef.current.focus();
      } else if (!confirmPassword && confirmPasswordRef.current) {
        confirmPasswordRef.current.focus();
      } else if (!firstName && firstNameRef.current) {
        firstNameRef.current.focus();
      } else if (!lastName && lastNameRef.current) {
        lastNameRef.current.focus();
      } else if (!pinCode && pinCodeRef.current) {
        pinCodeRef.current.focus();
      } else {
        handleRegister();
      }
    }
  };

  return (
    <div className="register-container">
      <img src={LogoDuet} alt="BNKaraoke.com Logo" className="register-logo" />
      <div className="register-card">
        <h2 className="register-title">Register</h2>
        {error && <p className="register-error">{error}</p>}
        <div className="register-form">
          <label htmlFor="userName">Phone Number</label>
          <input
            type="text"
            id="userName"
            value={formatPhoneNumber(userName)}
            onChange={handlePhoneChange}
            onKeyDown={handleKeyDown}
            placeholder="(123) 456-7890"
            aria-label="Phone number"
            className="register-input"
            ref={userNameRef}
            maxLength={14}
          />
          <label htmlFor="password">Password</label>
          <input
            type="password"
            id="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Enter password"
            aria-label="Password"
            className="register-input"
            ref={passwordRef}
          />
          <label htmlFor="confirmPassword">Confirm Password</label>
          <input
            type="password"
            id="confirmPassword"
            value={confirmPassword}
            onChange={(e) => setConfirmPassword(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Confirm password"
            aria-label="Confirm password"
            className="register-input"
            ref={confirmPasswordRef}
          />
          <label htmlFor="firstName">First Name</label>
          <input
            type="text"
            id="firstName"
            value={firstName}
            onChange={(e) => setFirstName(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="First name"
            aria-label="First name"
            className="register-input"
            ref={firstNameRef}
          />
          <label htmlFor="lastName">Last Name</label>
          <input
            type="text"
            id="lastName"
            value={lastName}
            onChange={(e) => setLastName(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Last name"
            aria-label="Last name"
            className="register-input"
            ref={lastNameRef}
          />
          <label htmlFor="pinCode">Enter the PIN Code Given to You by the Karaoke DJ</label>
          <input
            type="text"
            id="pinCode"
            value={pinCode}
            onChange={(e) => setPinCode(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="6-digit PIN code"
            aria-label="PIN code"
            className="register-input"
            ref={pinCodeRef}
            maxLength={6}
          />
          <button onClick={handleRegister} className="register-button">
            Register
          </button>
          <button onClick={() => navigate("/")} className="register-button secondary-button">
            Back to Login
          </button>
        </div>
        <p className="backlink">
          BPM data provided by <a href="https://getsongbpm.com" target="_blank" rel="noopener noreferrer">GetSongBPM</a>
        </p>
      </div>
    </div>
  );
};

export default RegisterPage;