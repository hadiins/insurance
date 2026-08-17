import { useEffect, useState } from "react";
import { Card, Col, Row, Statistic, Spin } from "antd";
import {
  CheckCircleOutlined,
  ClockCircleOutlined,
  TeamOutlined,
  UnorderedListOutlined,
} from "@ant-design/icons";
import { tasksApi, customersApi, type Task } from "../api/client";

export default function DashboardPage() {
  const [tasks, setTasks] = useState<Task[] | null>(null);
  const [customerCount, setCustomerCount] = useState<number | null>(null);

  useEffect(() => {
    tasksApi.list().then(setTasks);
    customersApi.list().then((list) => setCustomerCount(list.length));
  }, []);

  if (tasks === null || customerCount === null) {
    return <Spin size="large" style={{ display: "flex", justifyContent: "center", marginTop: 48 }} />;
  }

  const todoCount = tasks.filter((t) => t.status === "todo").length;
  const inProgressCount = tasks.filter((t) => t.status === "in_progress").length;
  const doneCount = tasks.filter((t) => t.status === "done").length;

  return (
    <Row gutter={[16, 16]}>
      <Col xs={24} sm={12} lg={6}>
        <Card>
          <Statistic
            title="کل وظایف"
            value={tasks.length}
            prefix={<UnorderedListOutlined />}
          />
        </Card>
      </Col>
      <Col xs={24} sm={12} lg={6}>
        <Card>
          <Statistic
            title="برای انجام"
            value={todoCount}
            valueStyle={{ color: "#8c8c8c" }}
            prefix={<ClockCircleOutlined />}
          />
        </Card>
      </Col>
      <Col xs={24} sm={12} lg={6}>
        <Card>
          <Statistic
            title="در حال انجام"
            value={inProgressCount}
            valueStyle={{ color: "#1677ff" }}
            prefix={<ClockCircleOutlined />}
          />
        </Card>
      </Col>
      <Col xs={24} sm={12} lg={6}>
        <Card>
          <Statistic
            title="انجام‌شده"
            value={doneCount}
            valueStyle={{ color: "#52c41a" }}
            prefix={<CheckCircleOutlined />}
          />
        </Card>
      </Col>
      <Col xs={24} sm={12} lg={6}>
        <Card>
          <Statistic title="تعداد مشتریان" value={customerCount} prefix={<TeamOutlined />} />
        </Card>
      </Col>
    </Row>
  );
}
