package main

import (
	"fmt"
	"io"
	"net/http"

	"github.com/gin-gonic/gin"
)

type QSOUploadRequest struct {
	ADIF      string `json:"adif" binding:"required"`
	Timestamp int64  `json:"timestamp" binding:"required"`
}

type RadioRequest struct {
	Key         *string  `json:"key"`
	Radio       string   `json:"radio"`
	Frequency   uint64   `json:"frequency"`
	Mode        string   `json:"mode"`
	FrequencyRx *uint64  `json:"frequencyRx"`
	ModeRx      *string  `json:"modeRx"`
	Power       *float32 `json:"power"`
}

func main() {
	r := gin.Default()

	r.POST("/adif", func(c *gin.Context) {
		var request QSOUploadRequest
		if err := c.ShouldBindJSON(&request); err != nil {
			fmt.Printf("Error while processing json: %s\n", err.Error())
			c.String(http.StatusBadRequest, "Invalid JSON: ")
			return
		}
		fmt.Printf("ADIF: %v\n", request)
		c.String(http.StatusOK, "OK")
	})

	r.POST("/decode", func(ctx *gin.Context) {
		res, err := io.ReadAll(ctx.Request.Body)
		if err != nil {
			fmt.Printf("Error while processing json: %s\n", err.Error())
			ctx.String(http.StatusBadRequest, "Invalid JSON: ")
			return
		}
		fmt.Println(string(res))
		ctx.String(http.StatusOK, "OK")
	})

	r.POST("/radio", func(c *gin.Context) {
		var request RadioRequest

		if err := c.ShouldBindJSON(&request); err != nil {
			fmt.Printf("Error while processing json: %s\n", err.Error())
			c.String(http.StatusBadRequest, "Invalid JSON: ")
			return
		}

		fmt.Printf("Radio name: %v\n", request)
		c.String(http.StatusOK, "OK")
	})

	r.Run(":8080")
}
