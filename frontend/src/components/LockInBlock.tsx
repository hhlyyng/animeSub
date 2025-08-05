import React, { useState } from "react";

const LoginBlock: React.FC = () => {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);

  const handleLogin = () => {
    console.log("Logging in with:", username, password);
  };

  const handleReset = () => {
    setUsername("");
    setPassword("");
  };

  return (
    <div
      style={{
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        height: "100vh", // 垂直居中
        backgroundColor: "#f4f4f4",
      }}
    >
      <div
        style={{
          backgroundColor: "white",
          padding: "2rem",
          borderRadius: "10px",
          boxShadow: "0px 4px 12px rgba(0,0,0,0.1)",
          width: "300px",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
        }}
      >
        {/* Logo + 名称 */}
        <img
          src="/logo.png"
          alt="Logo"
          style={{ width: "50px", marginBottom: "10px" }}
        />
        <h2>Anime Subscription</h2>

        {/* 用户名输入框 */}
        <input
          type="text"
          placeholder="Username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          style={{
            width: "100%",
            padding: "10px",
            margin: "10px 0",
            borderRadius: "5px",
            border: "1px solid #ccc",
          }}
        />

        {/* 密码输入框 + 显示按钮 */}
        <div style={{ display: "flex", width: "100%" }}>
          <input
            type={showPassword ? "text" : "password"}
            placeholder="Password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            style={{
              flex: 1,
              padding: "10px",
              margin: "10px 0",
              borderRadius: "5px 0 0 5px",
              border: "1px solid #ccc",
              borderRight: "none",
            }}
          />
          <button
            type="button"
            onClick={() => setShowPassword(!showPassword)}
            style={{
              padding: "10px",
              margin: "10px 0",
              borderRadius: "0 5px 5px 0",
              border: "1px solid #ccc",
              backgroundColor: "#eee",
              cursor: "pointer",
            }}
          >
            {showPassword ? "Hide" : "Show"}
          </button>
        </div>

        {/* 按钮 */}
        <div style={{ display: "flex", gap: "10px", marginTop: "10px" }}>
          <button
            onClick={handleLogin}
            style={{
              padding: "10px 20px",
              backgroundColor: "#4CAF50",
              color: "white",
              border: "none",
              borderRadius: "5px",
              cursor: "pointer",
            }}
          >
            Login
          </button>
          <button
            onClick={handleReset}
            style={{
              padding: "10px 20px",
              backgroundColor: "#f44336",
              color: "white",
              border: "none",
              borderRadius: "5px",
              cursor: "pointer",
            }}
          >
            Reset
          </button>
        </div>
      </div>
    </div>
  );
};

export default LoginBlock;