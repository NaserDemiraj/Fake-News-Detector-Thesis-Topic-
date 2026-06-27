"use client"

import { useState, useMemo } from "react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { BarChart, Bar, PieChart, Pie, Cell, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, LineChart, Line } from "recharts"
import { TrendingUp, CheckCircle, AlertCircle, Eye } from "lucide-react"

interface SavedAnalysis {
  id: string
  content: string
  type: "url" | "text"
  result: {
    verdict: string
    score: number
  }
  timestamp: number
  isFavorite: boolean
}

interface AnalyticsDashboardProps {
  analyses: SavedAnalysis[]
}

export default function AnalyticsDashboard({ analyses }: AnalyticsDashboardProps) {
  const stats = useMemo(() => {
    if (analyses.length === 0) {
      return {
        total: 0,
        fake: 0,
        legit: 0,
        uncertain: 0,
        fakePercent: 0,
        legitPercent: 0,
        uncertainPercent: 0,
        avgScore: 0,
        topDomains: [],
        verdictCounts: {},
        timelineData: [],
      }
    }

    const verdictCounts: Record<string, number> = {}
    let totalScore = 0
    let fakeCount = 0
    let legitCount = 0
    let uncertainCount = 0
    const domainCounts: Record<string, number> = {}
    const timelineData: Record<string, number> = {}

    analyses.forEach((analysis) => {
      // Count verdicts
      const verdict = analysis.result.verdict.toLowerCase()
      verdictCounts[verdict] = (verdictCounts[verdict] || 0) + 1

      if (verdict.includes("fake")) fakeCount++
      else if (verdict.includes("true")) legitCount++
      else uncertainCount++

      totalScore += analysis.result.score

      // Extract domain from content (if URL)
      if (analysis.type === "url") {
        try {
          const url = new URL(analysis.content)
          const domain = url.hostname
          domainCounts[domain] = (domainCounts[domain] || 0) + 1
        } catch (e) {
          // Invalid URL, skip
        }
      }

      // Timeline data (by date)
      const date = new Date(analysis.timestamp).toLocaleDateString()
      timelineData[date] = (timelineData[date] || 0) + 1
    })

    const total = analyses.length
    const topDomains = Object.entries(domainCounts)
      .sort((a, b) => b[1] - a[1])
      .slice(0, 5)
      .map(([domain, count]) => ({ domain, count }))

    const timelineArray = Object.entries(timelineData)
      .sort(([a], [b]) => new Date(a).getTime() - new Date(b).getTime())
      .map(([date, count]) => ({ date, analyses: count }))

    return {
      total,
      fake: fakeCount,
      legit: legitCount,
      uncertain: uncertainCount,
      fakePercent: ((fakeCount / total) * 100).toFixed(1),
      legitPercent: ((legitCount / total) * 100).toFixed(1),
      uncertainPercent: ((uncertainCount / total) * 100).toFixed(1),
      avgScore: (totalScore / total).toFixed(1),
      topDomains,
      verdictCounts,
      timelineData: timelineArray,
    }
  }, [analyses])

  const verdictData = [
    { name: "Likely Fake", value: stats.fake, fill: "#ef4444" },
    { name: "Likely True", value: stats.legit, fill: "#22c55e" },
    { name: "Uncertain", value: stats.uncertain, fill: "#eab308" },
  ].filter((item) => item.value > 0)

  return (
    <div className="w-full space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-3xl font-bold bg-gradient-to-r from-blue-600 to-purple-600 bg-clip-text text-transparent">
            Analytics Dashboard
          </h2>
          <p className="text-gray-600 mt-1">Overview of your news analysis activity</p>
        </div>
        <TrendingUp className="w-8 h-8 text-blue-600" />
      </div>

      {/* Stats Cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <Card className="border-2 border-gray-200 hover:border-blue-400 transition">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-gray-600">Total Analyses</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold">{stats.total}</div>
            <p className="text-xs text-gray-500 mt-1">All reviewed items</p>
          </CardContent>
        </Card>

        <Card className="border-2 border-red-200 hover:border-red-400 transition bg-red-50">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-red-600 flex items-center gap-2">
              <AlertCircle className="w-4 h-4" />
              Likely Fake
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold text-red-600">{stats.fake}</div>
            <p className="text-xs text-red-500 mt-1">{stats.fakePercent}% of total</p>
          </CardContent>
        </Card>

        <Card className="border-2 border-green-200 hover:border-green-400 transition bg-green-50">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-green-600 flex items-center gap-2">
              <CheckCircle className="w-4 h-4" />
              Likely True
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold text-green-600">{stats.legit}</div>
            <p className="text-xs text-green-500 mt-1">{stats.legitPercent}% of total</p>
          </CardContent>
        </Card>

        <Card className="border-2 border-yellow-200 hover:border-yellow-400 transition bg-yellow-50">
          <CardHeader className="pb-2">
            <CardTitle className="text-sm font-medium text-yellow-600">Average Score</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="text-3xl font-bold text-yellow-600">{stats.avgScore}</div>
            <p className="text-xs text-yellow-500 mt-1">out of 100</p>
          </CardContent>
        </Card>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Verdict Distribution Pie Chart */}
        {verdictData.length > 0 && (
          <Card className="border-2 border-gray-200">
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Eye className="w-5 h-5" />
                Verdict Distribution
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={300}>
                <PieChart>
                  <Pie
                    data={verdictData}
                    cx="50%"
                    cy="50%"
                    labelLine={false}
                    label={({ name, value, percent }) =>
                      `${name}: ${value} (${(percent * 100).toFixed(0)}%)`
                    }
                    outerRadius={80}
                    fill="#8884d8"
                    dataKey="value"
                  >
                    {verdictData.map((entry, index) => (
                      <Cell key={`cell-${index}`} fill={entry.fill} />
                    ))}
                  </Pie>
                  <Tooltip />
                </PieChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        )}

        {/* Timeline Chart */}
        {stats.timelineData.length > 0 && (
          <Card className="border-2 border-gray-200">
            <CardHeader>
              <CardTitle>Analysis Timeline</CardTitle>
            </CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={300}>
                <LineChart data={stats.timelineData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
                  <XAxis
                    dataKey="date"
                    tick={{ fontSize: 12 }}
                    angle={-45}
                    textAnchor="end"
                    height={80}
                  />
                  <YAxis tick={{ fontSize: 12 }} />
                  <Tooltip
                    contentStyle={{
                      backgroundColor: "#fff",
                      border: "1px solid #ccc",
                      borderRadius: "8px",
                    }}
                  />
                  <Line
                    type="monotone"
                    dataKey="analyses"
                    stroke="#3b82f6"
                    strokeWidth={2}
                    dot={{ fill: "#3b82f6", r: 4 }}
                    activeDot={{ r: 6 }}
                  />
                </LineChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        )}
      </div>

      {/* Top Domains */}
      {stats.topDomains.length > 0 && (
        <Card className="border-2 border-gray-200">
          <CardHeader>
            <CardTitle>Most Analyzed Domains</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {stats.topDomains.map((domain, index) => (
                <div
                  key={domain.domain}
                  className="flex items-center justify-between p-3 bg-gray-50 rounded-lg hover:bg-gray-100 transition"
                >
                  <div className="flex items-center gap-3">
                    <Badge variant="secondary" className="w-8 h-8 flex items-center justify-center rounded-full">
                      {index + 1}
                    </Badge>
                    <span className="font-medium text-gray-800">{domain.domain}</span>
                  </div>
                  <Badge className="bg-blue-100 text-blue-800">{domain.count} analyses</Badge>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      )}

      {/* Empty State */}
      {analyses.length === 0 && (
        <Card className="border-2 border-dashed border-gray-300 bg-gray-50">
          <CardContent className="py-12">
            <div className="text-center">
              <Eye className="w-12 h-12 text-gray-400 mx-auto mb-4" />
              <p className="text-gray-600 font-medium">No analyses yet</p>
              <p className="text-gray-500 text-sm mt-1">Start analyzing articles to see statistics here</p>
            </div>
          </CardContent>
        </Card>
      )}
    </div>
  )
}
